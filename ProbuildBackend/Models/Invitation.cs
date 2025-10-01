using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class Invitation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string InviterId { get; set; }

        [Required]
        [EmailAddress]
        public string InviteeEmail { get; set; }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        [Required]
        public string Token { get; set; }

        public bool IsAccepted { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        [ForeignKey("InviterId")]
        public virtual UserModel Inviter { get; set; }
    }
}