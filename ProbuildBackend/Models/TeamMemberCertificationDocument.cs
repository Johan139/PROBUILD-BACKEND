using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class TeamMemberCertificationDocument
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string TeamMemberId { get; set; } = null!;

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; } = null!;

        [Required]
        public string BlobUrl { get; set; } = null!;

        [MaxLength(255)]
        public string? ContentType { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(TeamMemberId))]
        public TeamMember TeamMember { get; set; } = null!;
    }
}
