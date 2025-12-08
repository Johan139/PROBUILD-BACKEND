using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class BudgetLineItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int JobId { get; set; }

        [Required]
        public string Category { get; set; } = "Other"; // e.g., "Labor", "Material", "Subcontractor", "Equipment"

        [Required]
        public string Item { get; set; } // Description, e.g., "Foundation Excavation"

        public string? Phase { get; set; } // e.g., "Groundwork & Foundation"

        public string? Trade { get; set; } // e.g., "Site Work", "Plumbing"

        public string? Vendor { get; set; } // Subcontractor/Supplier Name

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Quantity { get; set; }

        public string? Unit { get; set; } // e.g. "sqft", "ea"

        [Column(TypeName = "decimal(18,2)")]
        public decimal? UnitCost { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal EstimatedCost { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal ActualCost { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal ForecastToComplete { get; set; } = 0;

        public int PercentComplete { get; set; } = 0;

        public string? Status { get; set; } = "Pending"; // "Pending", "In Progress", "Completed", "Over Budget"

        public string? Notes { get; set; }

        public string? Source { get; set; } = "Manual"; // "Manual", "Subtask", "BOM", "Invoice"

        public string? SourceId { get; set; } // ID from the source system (e.g., Subtask ID)

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
