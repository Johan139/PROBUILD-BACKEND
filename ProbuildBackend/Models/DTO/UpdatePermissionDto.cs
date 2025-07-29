using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class UpdatePermissionsDto
    {
        [Required]
        public List<string> Permissions { get; set; }
    }
}
