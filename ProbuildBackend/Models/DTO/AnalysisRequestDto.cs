using System.ComponentModel.DataAnnotations;
using ProbuildBackend.Models.Enums;

namespace ProbuildBackend.Models.DTO
{
    public class AnalysisRequestDto
    {
        [Required]
        public AnalysisType AnalysisType { get; set; }

        [Required]
        public List<string> PromptKeys { get; set; }

        [Required]
        public List<string> DocumentUrls { get; set; }

        // Optional field for future use, e.g., to pass user context.
        public string UserContext { get; set; }
    }
}