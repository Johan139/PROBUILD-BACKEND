using System.ComponentModel.DataAnnotations;

namespace BuildigBackend.Models
{
    public class EmailLogEvent
    {
        [Key]
        public long Id { get; set; }

        public Guid EmailLogId { get; set; }

        [MaxLength(320)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Type { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; }

        [MaxLength(128)]
        public string? SgEventId { get; set; }

        [MaxLength(256)]
        public string? SmtpId { get; set; }

        [MaxLength(512)]
        public string? Reason { get; set; }

        [MaxLength(1024)]
        public string? Response { get; set; }

        [MaxLength(64)]
        public string? Ip { get; set; }

        [MaxLength(512)]
        public string? UserAgent { get; set; }

        [MaxLength(2048)]
        public string? Url { get; set; }

        public EmailLog? EmailLog { get; set; }
    }
}

