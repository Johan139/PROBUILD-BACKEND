using System.Text.Json;

namespace BuildigBackend.Models.DTO
{
    public class ScopeReviewPromptStatusDto
    {
        public bool HasParsedJson { get; set; }
        public DateTime? LastRunAt { get; set; }
        public string? LastSchemaVersion { get; set; }
        public string? LastFailureReason { get; set; }
    }

    public class ScopeReviewSnapshotDto
    {
        public JsonElement? ExecutiveSummary { get; set; }
        public JsonElement? Timeline { get; set; }
        public JsonElement? CostBreakdowns { get; set; }
        public ScopeReviewPromptStatusDto ExecutiveSummaryStatus { get; set; } = new();
        public ScopeReviewPromptStatusDto TimelineStatus { get; set; } = new();
        public ScopeReviewPromptStatusDto CostBreakdownsStatus { get; set; } = new();
    }
}

