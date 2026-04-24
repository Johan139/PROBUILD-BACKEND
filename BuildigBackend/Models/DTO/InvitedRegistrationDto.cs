using System.ComponentModel.DataAnnotations;

namespace BuildigBackend.Models.DTO
{
    public class InvitedRegistrationDto
    {
        [Required]
        public string Token { get; set; }

        [Required]
        public string Password { get; set; }

        public string PhoneNumber { get; set; }
    }
}

