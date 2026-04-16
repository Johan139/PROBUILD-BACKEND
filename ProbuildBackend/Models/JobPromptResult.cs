using System;

namespace ProbuildBackend.Models
{
    public class JobPromptResult
    {
        public int Id { get; set; }
        public int JobId { get; set; }
        public string PromptKey { get; set; } = string.Empty;
        public string? SchemaVersion { get; set; }
        public string RawResponse { get; set; } = string.Empty;
        public string? ParsedJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public JobModel? Job { get; set; }
    }
}
