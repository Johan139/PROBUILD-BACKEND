using System.Text.Json.Serialization;

public class SignalRMessage
{
    [JsonPropertyName("Id")]
    public long Id { get; set; }

    [JsonPropertyName("ConversationId")]
    public string ConversationId { get; set; }

    [JsonPropertyName("Role")]
    public string Role { get; set; }

    [JsonPropertyName("Content")]
    public string Content { get; set; }

    [JsonPropertyName("IsSummarized")]
    public bool IsSummarized { get; set; }

    [JsonPropertyName("Timestamp")]
    public DateTime Timestamp { get; set; }
}
