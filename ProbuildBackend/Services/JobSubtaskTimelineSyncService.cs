using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;

namespace ProbuildBackend.Services
{
    public class JobSubtaskTimelineSyncService : IJobSubtaskTimelineSyncService
    {
        private readonly ApplicationDbContext _context;

        private static readonly Regex JsonFenceRegex = new(
            "```json\\s*([\\s\\S]*?)\\s*```",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex TimelineHeadingRegex = new(
            "^###\\s*Phase\\s+(\\d+)\\s*:\\s*Timeline\\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex FullAnalysisHeaderRegex = new(
            "\\|\\s*Phase\\s*\\|\\s*Task\\s*\\|\\s*Duration(?:\\s*\\((?:Workdays|Days)\\))?\\s*\\|",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex NextPhaseSectionRegex = new(
            "^###\\s*Phase\\s+\\d+\\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex OutputSectionRegex = new(
            "^###\\s*\\*{0,2}Output\\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        public JobSubtaskTimelineSyncService(ApplicationDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task ReplaceSubtasksFromReportAsync(
            int jobId,
            string fullResponse,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrWhiteSpace(fullResponse))
            {
                return;
            }

            var rows = TimelineMarkdownParser.ParseRows(fullResponse, jobId);
            if (rows.Count == 0)
            {
                return;
            }

            var existing = await _context
                .JobSubtasks.Where(s => s.JobId == jobId)
                .ToListAsync(cancellationToken);

            if (existing.Count > 0)
            {
                _context.JobSubtasks.RemoveRange(existing);
            }

            await _context.JobSubtasks.AddRangeAsync(rows, cancellationToken);
        }

        private static class TimelineMarkdownParser
        {
            public static List<JobSubtasksModel> ParseRows(string report, int jobId)
            {
                var isSelected = TryGetIsSelectedFromReport(report);
                return isSelected
                    ? ParseSelectedLayout(report, jobId)
                    : ParseFullAnalysisLayout(report, jobId);
            }

            private static bool TryGetIsSelectedFromReport(string report)
            {
                var match = JsonFenceRegex.Match(report);
                if (!match.Success)
                {
                    return false;
                }

                try
                {
                    using var doc = JsonDocument.Parse(match.Groups[1].Value.Trim());
                    if (
                        doc.RootElement.TryGetProperty("isSelected", out var el)
                        && el.ValueKind == JsonValueKind.String
                    )
                    {
                        return string.Equals(
                            el.GetString(),
                            "true",
                            StringComparison.OrdinalIgnoreCase
                        );
                    }
                }
                catch (JsonException)
                {
                    // ignore
                }

                return false;
            }

            private static List<JobSubtasksModel> ParseSelectedLayout(string report, int jobId)
            {
                var lines = report.Split('\n');
                var tableStarted = false;
                var currentPhase = "";
                var inTimelineSection = false;
                int? timelinePromptNumber = null;
                var groups = new Dictionary<string, List<JobSubtasksModel>>(
                    StringComparer.OrdinalIgnoreCase
                );

                foreach (var rawLine in lines)
                {
                    var trimmedLine = rawLine.Trim();

                    if (!inTimelineSection)
                    {
                        var headingMatch = TimelineHeadingRegex.Match(trimmedLine);
                        if (headingMatch.Success)
                        {
                            inTimelineSection = true;
                            timelinePromptNumber = int.Parse(
                                headingMatch.Groups[1].Value,
                                CultureInfo.InvariantCulture
                            );
                        }
                    }

                    if (!inTimelineSection)
                    {
                        continue;
                    }

                    if (
                        trimmedLine.StartsWith("ready for the next prompt", StringComparison.OrdinalIgnoreCase)
                        && (
                            timelinePromptNumber == null
                            || trimmedLine.Contains(
                                timelinePromptNumber.Value.ToString(CultureInfo.InvariantCulture),
                                StringComparison.Ordinal
                            )
                        )
                    )
                    {
                        break;
                    }

                    if (tableStarted && NextPhaseSectionRegex.IsMatch(trimmedLine))
                    {
                        break;
                    }

                    if (tableStarted && OutputSectionRegex.IsMatch(trimmedLine))
                    {
                        break;
                    }

                    if (
                        trimmedLine.StartsWith("| Phase | Task |", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        tableStarted = true;
                        continue;
                    }

                    if (
                        !tableStarted
                        || !trimmedLine.StartsWith('|')
                        || trimmedLine.Contains("---", StringComparison.Ordinal)
                    )
                    {
                        continue;
                    }

                    var columns = SplitMarkdownRow(trimmedLine);
                    if (columns.Count < 6)
                    {
                        continue;
                    }

                    var phaseRaw = columns[0].Replace("**", "", StringComparison.Ordinal).Trim();
                    if (
                        phaseRaw.Contains(
                            "total project duration",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(phaseRaw))
                    {
                        currentPhase = phaseRaw;
                    }

                    var taskName = columns[1];
                    if (columns.Count < 6)
                    {
                        continue;
                    }

                    var durationStr = columns[3];
                    var startDateStr = columns[4];
                    var endDateStr = columns[5];
                    var costStr = columns.Count > 8 ? columns[8] : null;

                    var duration = ParseDurationDays(durationStr);
                    if (duration == null)
                    {
                        continue;
                    }

                    var endDate = ParseTimelineDate(endDateStr);
                    var startDate = ParseTimelineDate(startDateStr);
                    if (startDate == null && endDate != null && duration.Value > 0)
                    {
                        startDate = endDate.Value.AddDays(-duration.Value);
                    }
                    else if (startDate == null && endDate != null)
                    {
                        startDate = endDate;
                    }

                    if (startDate == null || endDate == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(currentPhase) || IsInvalidGroupTitle(currentPhase))
                    {
                        continue;
                    }

                    var taskClean = CleanTaskName(taskName);
                    if (string.IsNullOrWhiteSpace(taskClean))
                    {
                        continue;
                    }

                    if (!groups.TryGetValue(currentPhase, out var list))
                    {
                        list = new List<JobSubtasksModel>();
                        groups[currentPhase] = list;
                    }

                    list.Add(
                        CreateRow(
                            jobId,
                            CleanTaskName(currentPhase),
                            taskClean,
                            duration.Value,
                            startDate.Value,
                            endDate.Value,
                            costStr
                        )
                    );
                }

                return FlattenGroups(groups);
            }

            private static List<JobSubtasksModel> ParseFullAnalysisLayout(string report, int jobId)
            {
                var lines = report.Split('\n');
                var tableStarted = false;
                var currentPhase = "";
                var inTimelineSection = false;
                int? timelinePromptNumber = null;
                var groups = new Dictionary<string, List<JobSubtasksModel>>(
                    StringComparer.OrdinalIgnoreCase
                );

                foreach (var rawLine in lines)
                {
                    var trimmedLine = rawLine.Trim();

                    if (!inTimelineSection)
                    {
                        var headingMatch = TimelineHeadingRegex.Match(trimmedLine);
                        if (headingMatch.Success)
                        {
                            inTimelineSection = true;
                            timelinePromptNumber = int.Parse(
                                headingMatch.Groups[1].Value,
                                CultureInfo.InvariantCulture
                            );
                        }
                    }

                    if (!inTimelineSection)
                    {
                        continue;
                    }

                    if (
                        trimmedLine.StartsWith("ready for the next prompt", StringComparison.OrdinalIgnoreCase)
                        && (
                            timelinePromptNumber == null
                            || trimmedLine.Contains(
                                timelinePromptNumber.Value.ToString(CultureInfo.InvariantCulture),
                                StringComparison.Ordinal
                            )
                        )
                    )
                    {
                        break;
                    }

                    if (tableStarted && NextPhaseSectionRegex.IsMatch(trimmedLine))
                    {
                        break;
                    }

                    if (tableStarted && OutputSectionRegex.IsMatch(trimmedLine))
                    {
                        break;
                    }

                    if (!tableStarted && FullAnalysisHeaderRegex.IsMatch(trimmedLine))
                    {
                        tableStarted = true;
                        continue;
                    }

                    if (
                        !tableStarted
                        || !trimmedLine.StartsWith('|')
                        || trimmedLine.Contains("---", StringComparison.Ordinal)
                    )
                    {
                        continue;
                    }

                    var columns = SplitMarkdownRow(trimmedLine);
                    if (columns.Count < 6)
                    {
                        continue;
                    }

                    var phaseRaw = columns[0];
                    var taskName = columns[1];
                    if (
                        phaseRaw.Contains("Financial Milestone", StringComparison.OrdinalIgnoreCase)
                        || taskName.Contains("Financial Milestone", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        continue;
                    }

                    var phaseName = phaseRaw.Replace("**", "", StringComparison.Ordinal)
                        .Replace("\\", "", StringComparison.Ordinal)
                        .Trim();
                    if (!string.IsNullOrEmpty(phaseName))
                    {
                        currentPhase = phaseName;
                    }

                    if (string.IsNullOrWhiteSpace(taskName))
                    {
                        continue;
                    }

                    var durationStr = columns[2];
                    var startDateStr = columns[4];
                    var endDateStr = columns[5];
                    var costStr = columns.Count > 8 ? columns[8] : null;

                    var duration = ParseDurationDays(durationStr);
                    if (duration == null)
                    {
                        continue;
                    }

                    if (!ParseTimelineDate(startDateStr, out var startDate))
                    {
                        continue;
                    }

                    if (!ParseTimelineDate(endDateStr, out var endDate))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(currentPhase) || IsInvalidGroupTitle(currentPhase))
                    {
                        continue;
                    }

                    var taskClean = CleanTaskName(taskName);
                    if (string.IsNullOrWhiteSpace(taskClean))
                    {
                        continue;
                    }

                    if (!groups.TryGetValue(currentPhase, out var list))
                    {
                        list = new List<JobSubtasksModel>();
                        groups[currentPhase] = list;
                    }

                    list.Add(
                        CreateRow(
                            jobId,
                            CleanTaskName(currentPhase),
                            taskClean,
                            duration.Value,
                            startDate,
                            endDate,
                            costStr
                        )
                    );
                }

                return FlattenGroups(groups);
            }

            private static List<JobSubtasksModel> FlattenGroups(
                Dictionary<string, List<JobSubtasksModel>> groups
            )
            {
                var result = new List<JobSubtasksModel>();
                foreach (var kv in groups)
                {
                    if (IsInvalidGroupTitle(kv.Key))
                    {
                        continue;
                    }

                    result.AddRange(kv.Value);
                }

                return result;
            }

            private static JobSubtasksModel CreateRow(
                int jobId,
                string groupTitle,
                string task,
                int days,
                DateTime startDate,
                DateTime endDate,
                string? costStr
            )
            {
                var cost = ParseCost(costStr);
                var detailsDict = new Dictionary<string, object?> { ["source"] = "timeline" };
                if (cost > 0)
                {
                    detailsDict["cost"] = cost;
                }

                return new JobSubtasksModel
                {
                    JobId = jobId,
                    GroupTitle = groupTitle,
                    Task = task,
                    Days = days,
                    StartDate = startDate.Date,
                    EndDate = endDate.Date,
                    Status = "Pending",
                    Deleted = false,
                    DetailsJson = JsonSerializer.Serialize(detailsDict),
                };
            }

            private static List<string> SplitMarkdownRow(string line)
            {
                var parts = line.Split('|');
                if (parts.Length < 3)
                {
                    return new List<string>();
                }

                return parts
                    .Skip(1)
                    .Take(parts.Length - 2)
                    .Select(c => c.Trim())
                    .ToList();
            }

            private static string CleanTaskName(string name)
            {
                if (string.IsNullOrEmpty(name))
                {
                    return "";
                }

                var t = name.Trim();
                if (t.StartsWith("**", StringComparison.Ordinal))
                {
                    t = t[2..];
                }

                if (t.EndsWith("**", StringComparison.Ordinal))
                {
                    t = t[..^2];
                }

                return t.Trim();
            }

            private static bool IsInvalidGroupTitle(string title)
            {
                var t = (title ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(t))
                {
                    return true;
                }

                if (t == "project day")
                {
                    return true;
                }

                if (Regex.IsMatch(t, "^day\\s*\\d+\\b"))
                {
                    return true;
                }

                if (Regex.IsMatch(t, "^days\\s*\\d+\\s*-\\s*\\d+\\b"))
                {
                    return true;
                }

                if (t.Contains("ready for the next prompt", StringComparison.Ordinal))
                {
                    return true;
                }

                var looksLikeMaterialItem =
                    t.Contains("cabinet", StringComparison.Ordinal)
                    || t.Contains("tile", StringComparison.Ordinal)
                    || t.Contains("quartz", StringComparison.Ordinal)
                    || t.Contains("shower", StringComparison.Ordinal)
                    || t.Contains("door", StringComparison.Ordinal)
                    || t.Contains("vinyl", StringComparison.Ordinal);

                return looksLikeMaterialItem;
            }

            private static int? ParseDurationDays(string? durationStr)
            {
                var raw = (durationStr ?? "").Trim();
                if (string.IsNullOrEmpty(raw))
                {
                    return null;
                }

                var match = Regex.Match(raw, "\\d{1,4}");
                if (!match.Success)
                {
                    return null;
                }

                if (
                    !int.TryParse(
                        match.Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var value
                    )
                )
                {
                    return null;
                }

                if (value <= 0 || value > 365)
                {
                    return null;
                }

                return value;
            }

            private static DateTime? ParseTimelineDate(string? dateStr)
            {
                if (
                    string.IsNullOrWhiteSpace(dateStr)
                    || dateStr.Trim() == "-"
                    || dateStr.Contains("assumed complete", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return null;
                }

                if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                {
                    return d.Date;
                }

                if (DateTime.TryParse(dateStr, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out d))
                {
                    return d.Date;
                }

                return null;
            }

            private static bool ParseTimelineDate(string? dateStr, out DateTime date)
            {
                var parsed = ParseTimelineDate(dateStr);
                if (parsed == null)
                {
                    date = default;
                    return false;
                }

                date = parsed.Value;
                return true;
            }

            private static decimal ParseCost(string? costStr)
            {
                if (string.IsNullOrWhiteSpace(costStr))
                {
                    return 0;
                }

                var t = costStr.Trim();
                if (
                    t.Equals("n/a", StringComparison.OrdinalIgnoreCase)
                    || t == "-"
                )
                {
                    return 0;
                }

                var numeric = Regex.Replace(t, "[^0-9.]", "");
                return decimal.TryParse(
                    numeric,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var v
                )
                    ? v
                    : 0;
            }
        }
    }
}
