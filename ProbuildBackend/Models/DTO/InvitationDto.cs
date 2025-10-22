using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class InvitationDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }
        [Required]
        public string FirstName { get; set; }
        [Required]
        public string LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Message { get; set; }
    }
}