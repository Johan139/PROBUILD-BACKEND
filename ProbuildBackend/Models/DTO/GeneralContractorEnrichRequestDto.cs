using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class GeneralContractorEnrichRequestDto
    {
        [Required]
        public string CompanyName { get; set; } = string.Empty;

        public string? Domain { get; set; }
        public int? JobId { get; set; }
    }
}
