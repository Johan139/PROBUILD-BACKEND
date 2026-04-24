using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BuildigBackend.Models
{
    public class ExternalContact
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(50)]
        public string? Source { get; set; } = "Apollo";

        [MaxLength(200)]
        public string? ExternalId { get; set; }

        [Required]
        public int ExternalCompanyId { get; set; }

        [ForeignKey(nameof(ExternalCompanyId))]
        public ExternalCompany? ExternalCompany { get; set; }

        [MaxLength(120)]
        public string? FirstName { get; set; }

        [MaxLength(120)]
        public string? LastName { get; set; }

        [MaxLength(250)]
        public string? FullName { get; set; }

        [MaxLength(250)]
        public string? Title { get; set; }

        [MaxLength(320)]
        public string? Email { get; set; }

        [MaxLength(120)]
        public string? Phone { get; set; }

        [MaxLength(500)]
        public string? LinkedinUrl { get; set; }

        [MaxLength(1000)]
        public string? Headline { get; set; }

        [MaxLength(4000)]
        public string? RawPayloadJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}


