using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class RenovationAnalysisRequestDto
    {
        [Required]
        public string UserId { get; set; }
        // Add other properties from JobModel
    }
}
