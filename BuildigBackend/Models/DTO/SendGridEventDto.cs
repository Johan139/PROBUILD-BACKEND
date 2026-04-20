using System.Text.Json.Serialization;

namespace BuildigBackend.Models.DTO
{
    public class SendGridEventDto
    {
        [JsonPropertyName("event")]
        public string? Event { get; set; }
        [JsonPropertyName("email")]
        public string? Email { get; set; }
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }
        [JsonPropertyName("sg_event_id")]
        public string? SgEventId { get; set; }
        [JsonPropertyName("smtp-id")]
        public string? SmtpId { get; set; }
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
        [JsonPropertyName("response")]
        public string? Response { get; set; }
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }
        [JsonPropertyName("useragent")]
        public string? UserAgent { get; set; }
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        // Custom args come as top-level fields, not nested
        [JsonPropertyName("emailLogId")]
        public string? EmailLogId { get; set; }

        [JsonPropertyName("templateId")]
        public string? TemplateId { get; set; }
    }
}

