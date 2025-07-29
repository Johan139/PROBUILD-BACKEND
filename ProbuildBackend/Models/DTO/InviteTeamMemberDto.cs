using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class InviteTeamMemberDto
    {
        [Required]
        public string FirstName { get; set; }

        [Required]
        public string LastName { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        public string Role { get; set; }
    }
}