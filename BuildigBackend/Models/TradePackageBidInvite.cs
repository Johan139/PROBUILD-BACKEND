using System.ComponentModel.DataAnnotations;

namespace BuildigBackend.Models
{
    public class TradePackageBidInvite
    {
        [Key]
        public int Id { get; set; }

        public int JobId { get; set; }

        public int TradePackageId { get; set; }

        public int? ExternalCompanyId { get; set; }

        public int? ExternalContactId { get; set; }

        [MaxLength(320)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(250)]
        public string? ContactName { get; set; }

        [MaxLength(300)]
        public string? CompanyName { get; set; }

        [MaxLength(50)]
        public string Status { get; set; } = "Selected";

        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

