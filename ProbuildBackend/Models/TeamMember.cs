using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class TeamMember
    {
        [Key]
        public string Id { get; set; }

        [Required]
        public string InviterId { get; set; }

        [Required]
        public string FirstName { get; set; }

        [Required]
        public string LastName { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        public string? PhoneNumber { get; set; }

        [Required]
        public string Role { get; set; }

        public string? PasswordHash { get; set; }

        [Required]
        public string Status { get; set; } = "Invited";

        public string? InvitationToken { get; set; }

        public DateTime? TokenExpiration { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("InviterId")]
        public virtual UserModel Inviter { get; set; }

        public ICollection<TeamMemberPermission> TeamMemberPermissions { get; set; }
    }
}
