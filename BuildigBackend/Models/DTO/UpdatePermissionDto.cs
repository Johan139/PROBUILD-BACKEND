using System.ComponentModel.DataAnnotations;

namespace BuildigBackend.Models.DTO
{
    public class UpdatePermissionsDto
    {
        [Required]
        public List<string> Permissions { get; set; }
    }
}

