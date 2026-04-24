public class Message
{
    public long Id { get; set; }
    public string ConversationId { get; set; }
    public string Role { get; set; } // "user" or "model"
    public string Content { get; set; }
    public bool IsSummarized { get; set; }
    public DateTime Timestamp { get; set; }
}
