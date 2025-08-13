public class Conversation
{
    public string Id { get; set; }
    public string UserId { get; set; }
    public string Title { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ConversationSummary { get; set; }
    public virtual ICollection<ConversationPromptKey> PromptKeys { get; set; } = new List<ConversationPromptKey>();
}
