using System.ComponentModel.DataAnnotations;

namespace BuildigBackend.Models.DTO
{
    public class SubcontractorDiscoveryRequestDto
    {
        public int? TradePackageId { get; set; }
        public int? JobId { get; set; }

        [Required]
        public string TradeName { get; set; } = string.Empty;

        public string? City { get; set; }
        public string? State { get; set; }
        public int RadiusMiles { get; set; } = 25;
        public int Limit { get; set; } = 12;
        public string? SearchText { get; set; }
    }
}

