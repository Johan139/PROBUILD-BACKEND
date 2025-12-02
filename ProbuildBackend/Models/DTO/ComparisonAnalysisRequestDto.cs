using System.ComponentModel.DataAnnotations;
using ProbuildBackend.Models.Enums;

namespace ProbuildBackend.Models.DTO
{
    public class ComparisonAnalysisRequestDto
    {
        [Required]
        public string UserId { get; set; }

        [Required]
        public ComparisonType ComparisonType { get; set; } // Enum: Vendor, Subcontractor
    }
}
