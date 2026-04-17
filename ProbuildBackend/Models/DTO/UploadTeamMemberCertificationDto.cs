using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class UploadTeamMemberCertificationDto
    {
        [Required]
        public List<IFormFile> Files { get; set; } = new();

        public string? ConnectionId { get; set; }
    }
}
