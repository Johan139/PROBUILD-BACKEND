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

        public string UserContext { get; set; }

        public string? UserContextFileUrl { get; set; }

        public int JobId { get; set; }

        public string UserId { get; set; }

        public bool GenerateDetailsWithAi { get; set; }

        public string? ConversationId { get; set; }

        public string BudgetLevel { get; set; }
    }
}
