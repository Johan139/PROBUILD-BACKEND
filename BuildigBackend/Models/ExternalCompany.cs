using System.ComponentModel.DataAnnotations;

namespace BuildigBackend.Models
{
    public class ExternalCompany
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(50)]
        public string? Source { get; set; } = "Apollo";

        [MaxLength(200)]
        public string? ExternalId { get; set; }

        [MaxLength(300)]
        public string? Name { get; set; } = string.Empty;

        [MaxLength(300)]
        public string? Domain { get; set; }

        [MaxLength(500)]
        public string? WebsiteUrl { get; set; }

        [MaxLength(500)]
        public string? LinkedinUrl { get; set; }

        [MaxLength(120)]
        public string? Phone { get; set; }

        [MaxLength(150)]
        public string? City { get; set; }

        [MaxLength(150)]
        public string? State { get; set; }

        [MaxLength(150)]
        public string? Country { get; set; }

        public decimal? Latitude { get; set; }

        public decimal? Longitude { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        [MaxLength(200)]
        public string? Industry { get; set; }

        public string? Email { get; set; }

        [MaxLength(50)]
        public string? EmailConfidence { get; set; }

        public int? EmployeeCount { get; set; }

        public int? FoundedYear { get; set; }

        public DateTime? LastEnrichedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ExternalContact> Contacts { get; set; } = new List<ExternalContact>();
    }
}

