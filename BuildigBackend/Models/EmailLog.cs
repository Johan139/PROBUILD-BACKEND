using System.ComponentModel.DataAnnotations;

namespace BuildigBackend.Models
{
    public class EmailLog
    {
        [Key]
        public Guid Id { get; set; }

        [MaxLength(320)]
        public string ToEmail { get; set; } = string.Empty;

        [MaxLength(320)]
        public string? FromEmail { get; set; }

        [MaxLength(998)]
        public string? Subject { get; set; }

        public int? TemplateId { get; set; }

        [MaxLength(256)]
        public string? TemplateName { get; set; }

        [MaxLength(64)]
        public string Provider { get; set; } = "sendgrid";

        [MaxLength(64)]
        public string? LastEventType { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastEventAt { get; set; }

        public ICollection<EmailLogEvent> Events { get; set; } = new List<EmailLogEvent>();
    }
}

