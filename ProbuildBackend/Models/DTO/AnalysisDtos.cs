using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class RenovationAnalysisRequest
    {
        [Required]
        public string UserId { get; set; }
        // Add other relevant properties from JobModel as needed
    }

    public class AnalysisResponse
    {
        public string AnalysisResult { get; set; }
        public string ConversationId { get; set; }
    }

    public class ComparisonAnalysisRequest
    {
        [Required]
        public string UserId { get; set; }
        [Required]
        public ComparisonType ComparisonType { get; set; } // Enum: Vendor, Subcontractor
    }

    public enum ComparisonType
    {
        Vendor,
        Subcontractor
    }
}