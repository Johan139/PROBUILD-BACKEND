using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BuildigBackend.Models
{
    public class JobAnalysisState
    {
        [Key]
        public int Id { get; set; }

        public int JobId { get; set; }

        public int CurrentStep { get; set; }

        public int TotalSteps { get; set; }

        public string? StatusMessage { get; set; }

        public bool IsComplete { get; set; }

        public bool HasFailed { get; set; }

        public string? ErrorMessage { get; set; }

        // Stores JSON blob of accumulated data (rooms, metadata, etc.)
        public string? ExtractedDataJson { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}

