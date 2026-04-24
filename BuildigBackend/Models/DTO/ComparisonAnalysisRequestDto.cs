using System.ComponentModel.DataAnnotations;
using BuildigBackend.Models.Enums;

namespace BuildigBackend.Models.DTO
{
    public class ComparisonAnalysisRequestDto
    {
        [Required]
        public string UserId { get; set; }

        [Required]
        public ComparisonType ComparisonType { get; set; } // Enum: Vendor, Subcontractor
    }
}

