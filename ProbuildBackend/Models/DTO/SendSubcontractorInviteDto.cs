using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class SendSubcontractorInviteDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string? ContactName { get; set; }

        public string? PhoneNumber { get; set; }

        public int? JobId { get; set; }

        public int? TradePackageId { get; set; }

        public string? TradeName { get; set; }

        public string? Category { get; set; }

        public string? ScopeOfWork { get; set; }

        public decimal? Budget { get; set; }

        public bool AlsoMarketplace { get; set; }
    }
}

