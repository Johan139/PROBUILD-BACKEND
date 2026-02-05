using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class TradePackage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int JobId { get; set; }

        [ForeignKey("JobId")]
        public JobModel Job { get; set; }

        [Required]
        public string TradeName { get; set; }

        public string? Category { get; set; } // "Trade", "Vendor", "Supplier", "Equipment"

        public string? ScopeOfWork { get; set; }

        public decimal Budget { get; set; }

        public string? Status { get; set; } // "Draft", "Posted", "Awarded"

        public decimal EstimatedManHours { get; set; }

        public decimal HourlyRate { get; set; }

        public string? EstimatedDuration { get; set; }

        public string? CsiCode { get; set; }

        public bool PostedToMarketplace { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
