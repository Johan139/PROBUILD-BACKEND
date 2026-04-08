using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Services
{
    public class ChatKnowledgeResolverService
    {
        private readonly IPromptManagerService _promptManagerService;
        private readonly ILogger<ChatKnowledgeResolverService> _logger;

        private static readonly string[] CoreKnowledgeFiles =
        [
            "build-ig-ai-instructions.md",
            "build-ig-product-overview.md",
        ];

        private static readonly ConcurrentDictionary<
            string,
            List<string>
        > _conversationKnowledgeCache = new();

        public ChatKnowledgeResolverService(
            IPromptManagerService promptManagerService,
            ILogger<ChatKnowledgeResolverService> logger
        )
        {
            _promptManagerService = promptManagerService;
            _logger = logger;
        }

        public async Task<ChatKnowledgeResolutionResult> ResolveAsync(
            string conversationScopeKey,
            ChatKnowledgeContextRequest request
        )
        {
            var basePrompt = await _promptManagerService.GetPromptAsync(
                request.UserType ?? string.Empty,
                "generic-prompt.txt"
            );

            var selectedKnowledgeFiles = SelectKnowledgeFiles(conversationScopeKey, request);
            var knowledgeMap = await _promptManagerService.GetKnowledgeFilesAsync(
                selectedKnowledgeFiles
            );

            var sb = new StringBuilder();
            sb.AppendLine(basePrompt.Trim());
            sb.AppendLine();
            sb.AppendLine("--- BUILD IG KNOWLEDGE CONTEXT ---");

            foreach (var file in selectedKnowledgeFiles)
            {
                if (
                    !knowledgeMap.TryGetValue(file, out var content)
                    || string.IsNullOrWhiteSpace(content)
                )
                {
                    continue;
                }

                sb.AppendLine();
                sb.AppendLine($"[Knowledge File: {file}]");
                sb.AppendLine(content.Trim());
            }

            sb.AppendLine();
            sb.AppendLine("--- RUNTIME CONTEXT ---");
            if (!string.IsNullOrWhiteSpace(request.UserType))
                sb.AppendLine($"User Type: {request.UserType}");
            if (!string.IsNullOrWhiteSpace(request.CurrentRoute))
                sb.AppendLine($"Current Route: {request.CurrentRoute}");
            if (!string.IsNullOrWhiteSpace(request.CurrentFeature))
                sb.AppendLine($"Current Feature: {request.CurrentFeature}");
            if (!string.IsNullOrWhiteSpace(request.CurrentStage))
                sb.AppendLine($"Current Stage: {request.CurrentStage}");
            if (!string.IsNullOrWhiteSpace(request.ProjectName))
                sb.AppendLine($"Project Name: {request.ProjectName}");
            if (request.PromptKeys.Any())
                sb.AppendLine($"Selected Prompt Keys: {string.Join(", ", request.PromptKeys)}");

            return new ChatKnowledgeResolutionResult
            {
                BasePrompt = basePrompt,
                SelectedKnowledgeFiles = selectedKnowledgeFiles,
                ComposedSystemPrompt = sb.ToString(),
            };
        }

        private List<string> SelectKnowledgeFiles(
            string conversationScopeKey,
            ChatKnowledgeContextRequest request
        )
        {
            var selected = new HashSet<string>(
                CoreKnowledgeFiles,
                StringComparer.OrdinalIgnoreCase
            );

            var message = (request.UserMessage ?? string.Empty).ToLowerInvariant();
            var helpIntent = (request.HelpIntent ?? string.Empty).ToLowerInvariant();
            var stage = (request.CurrentStage ?? string.Empty).ToLowerInvariant();
            var route = (request.CurrentRoute ?? string.Empty).ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(request.CurrentFeature))
            {
                selected.Add("build-ig-roles-and-workflows.md");
            }

            if (!string.IsNullOrWhiteSpace(helpIntent))
            {
                selected.UnionWith(MapHelpIntentToKnowledgeFiles(helpIntent));
            }

            if (message.Contains("what can you help") || message.Contains("what do you do"))
            {
                selected.Add("build-ig-feature-guides.md");
            }

            selected.UnionWith(MapMessageToKnowledgeFiles(message));

            if (
                message.Contains("new project")
                || message.Contains("start a project")
                || route.Contains("new-project")
            )
            {
                selected.Add("features/build-ig-new-project.md");
            }

            if (message.Contains("quote") || message.Contains("invoice") || route.Contains("quote"))
            {
                selected.Add("features/build-ig-quotes-and-invoices.md");
            }

            if (
                message.Contains("marketplace")
                || message.Contains("find work")
                || route.Contains("find-work")
            )
            {
                selected.Add("features/build-ig-marketplace.md");
            }

            if (message.Contains("dashboard") || route.Contains("dashboard"))
            {
                selected.Add("features/build-ig-dashboard.md");
            }

            if (message.Contains("project") && route.Contains("my-projects"))
            {
                selected.Add("features/build-ig-project-portfolio.md");
            }

            if (!string.IsNullOrWhiteSpace(stage))
            {
                selected.Add("features/build-ig-project-lifecycle.md");
                selected.UnionWith(MapStageToKnowledgeFiles(stage));
            }

            if (request.UserMessage is not null && IsGenericTaskRequest(helpIntent, message))
            {
                selected.Remove("build-ig-feature-guides.md");
                selected.RemoveWhere(x =>
                    x.StartsWith("features/", StringComparison.OrdinalIgnoreCase)
                );
            }

            var prior = _conversationKnowledgeCache.GetOrAdd(
                conversationScopeKey,
                _ => new List<string>()
            );
            if (
                selected.Count <= CoreKnowledgeFiles.Length
                && prior.Count > 0
                && !IsGenericTaskRequest(helpIntent, message)
            )
            {
                foreach (var item in prior.Take(2))
                {
                    selected.Add(item);
                }
            }

            var finalSelection = selected.ToList();
            _conversationKnowledgeCache[conversationScopeKey] = finalSelection;

            _logger.LogInformation(
                "Resolved knowledge files for {ConversationScopeKey}: {Files}",
                conversationScopeKey,
                string.Join(", ", finalSelection)
            );

            return finalSelection;
        }

        private static IEnumerable<string> MapStageToKnowledgeFiles(string stage)
        {
            return stage switch
            {
                "initiation" => ["features/build-ig-phase-initiation.md"],
                "preliminary_scope" => ["features/build-ig-phase-preliminary-scope-review.md"],
                "detailed_takeoff" => ["features/build-ig-phase-detailed-estimating-takeoff.md"],
                "contract_award" => ["features/build-ig-phase-contract-award-execution.md"],
                "pre_construction" => ["features/build-ig-phase-pre-construction-compliance.md"],
                "bid_solicitation" => ["features/build-ig-bid-solicitation.md"],
                "trade_award" => ["features/build-ig-trade-award.md"],
                "mobilization" => ["features/build-ig-phase-mobilization.md"],
                "construction_live" => ["features/build-ig-phase-construction-execution.md"],
                "closeout" => ["features/build-ig-phase-closeout-handover.md"],
                _ => [],
            };
        }

        private static IEnumerable<string> MapHelpIntentToKnowledgeFiles(string helpIntent)
        {
            return helpIntent switch
            {
                "general-build-ig-help" => ["build-ig-feature-guides.md"],
                "new-project" => ["features/build-ig-new-project.md"],
                "project-lifecycle" => ["features/build-ig-project-lifecycle.md"],
                "quotes-invoices" => ["features/build-ig-quotes-and-invoices.md"],
                "marketplace" => ["features/build-ig-marketplace.md"],
                "project-portfolio" => ["features/build-ig-project-portfolio.md"],
                "phase-help" => ["features/build-ig-project-lifecycle.md"],
                "generic-writing-help" => [],
                _ => [],
            };
        }

        private static IEnumerable<string> MapMessageToKnowledgeFiles(string message)
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (
                Regex.IsMatch(
                    message,
                    @"\b(new project|start (a )?new project|create (a )?new project|upload (my )?blueprints?)\b"
                )
            )
            {
                selected.Add("features/build-ig-new-project.md");
            }

            if (
                Regex.IsMatch(
                    message,
                    @"\b(lifecycle|construction workflow|different phases|project phases|workflow phases|stages of the project)\b"
                )
            )
            {
                selected.Add("features/build-ig-project-lifecycle.md");
            }

            if (Regex.IsMatch(message, @"\b(preliminary scope|scope review)\b"))
            {
                selected.Add("features/build-ig-phase-preliminary-scope-review.md");
            }

            if (
                Regex.IsMatch(
                    message,
                    @"\b(detailed takeoff|detailed estimating|takeoff|bill of materials|bom|value engineering)\b"
                )
            )
            {
                selected.Add("features/build-ig-phase-detailed-estimating-takeoff.md");
            }

            if (
                Regex.IsMatch(
                    message,
                    @"\b(contract award|contract execution|generate contract|signed contract)\b"
                )
            )
            {
                selected.Add("features/build-ig-phase-contract-award-execution.md");
            }

            if (
                Regex.IsMatch(
                    message,
                    @"\b(pre-construction|compliance|permits|permit documents)\b"
                )
            )
            {
                selected.Add("features/build-ig-phase-pre-construction-compliance.md");
            }

            if (
                Regex.IsMatch(
                    message,
                    @"\b(subcontractor bid solicitation|bid solicitation|solicitation|trade packages|post to marketplace|direct invites|invite subcontractors)\b"
                )
            )
            {
                selected.Add("features/build-ig-bid-solicitation.md");
            }

            if (
                Regex.IsMatch(
                    message,
                    @"\b(trade award|award bids|compare bids|choose contractor|award package)\b"
                )
            )
            {
                selected.Add("features/build-ig-trade-award.md");
            }

            if (
                Regex.IsMatch(
                    message,
                    @"\b(mobilization|go live|assign your team|construction start date)\b"
                )
            )
            {
                selected.Add("features/build-ig-phase-mobilization.md");
            }

            if (
                Regex.IsMatch(
                    message,
                    @"\b(construction execution|construction live|live construction|execution tasks)\b"
                )
            )
            {
                selected.Add("features/build-ig-phase-construction-execution.md");
            }

            if (
                Regex.IsMatch(
                    message,
                    @"\b(closeout|handover|archive project|final inspections|warranty handover|punch list)\b"
                )
            )
            {
                selected.Add("features/build-ig-phase-closeout-handover.md");
            }

            if (Regex.IsMatch(message, @"\b(quote|invoice|send to client|quotation)\b"))
            {
                selected.Add("features/build-ig-quotes-and-invoices.md");
            }

            if (
                Regex.IsMatch(
                    message,
                    @"\b(marketplace|find work|saved jobs|job alerts|my bids|job postings)\b"
                )
            )
            {
                selected.Add("features/build-ig-marketplace.md");
            }

            if (
                Regex.IsMatch(
                    message,
                    @"\b(dashboard|recent projects|action points|weather widget)\b"
                )
            )
            {
                selected.Add("features/build-ig-dashboard.md");
            }

            if (
                Regex.IsMatch(
                    message,
                    @"\b(my projects|project portfolio|view projects|manage projects)\b"
                )
            )
            {
                selected.Add("features/build-ig-project-portfolio.md");
            }

            return selected;
        }

        private static bool IsGenericTaskRequest(string helpIntent, string message)
        {
            if (helpIntent == "generic-writing-help")
            {
                return true;
            }

            return message.Contains("draft an email")
                || message.Contains("write an email")
                || message.Contains("summarise")
                || message.Contains("summarize")
                || message.Contains("rewrite this")
                || message.Contains("improve this message");
        }
    }
}
