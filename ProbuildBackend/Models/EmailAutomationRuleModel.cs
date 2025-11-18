using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class EmailAutomationRuleModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string RuleName { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string ConditionKey { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        public int DelayHours { get; set; } = 24;

        [Required]
        [ForeignKey(nameof(EmailTemplate))]
        public int TemplateId { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(100)]
        public string? CreatedBy { get; set; }

        [Required]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [MaxLength(100)]
        public string? ModifiedBy { get; set; }

        public DateTime? ModifiedDate { get; set; }

        public string? CtaUrl { get; set; }
        public string? SecondaryUrl { get; set; }
        public string? UpgradeUrl { get; set; }
        public string? BookLink { get; set; }

        // 🔗 Navigation property
        public virtual EmailTemplate? EmailTemplate { get; set; }
    }
}
