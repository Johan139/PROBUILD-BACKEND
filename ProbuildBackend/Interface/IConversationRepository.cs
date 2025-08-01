namespace ProbuildBackend.Interface
{
    public interface IConversationRepository
    {
        Task<Conversation?> GetConversationAsync(string conversationId);
        Task<string> CreateConversationAsync(string userId, string title);
        Task<IEnumerable<Conversation>> GetByUserIdAsync(string userId);
        Task<List<Message>> GetMessagesAsync(string conversationId, bool includeSummarized = true);
        Task<List<Message>> GetUnsummarizedMessagesAsync(string conversationId);
        Task AddMessageAsync(Message message);
        Task UpdateConversationSummaryAsync(string conversationId, string? newSummary);
        Task MarkMessagesAsSummarizedAsync(IEnumerable<long> messageIds);
        Task UpdateConversationTitleAsync(string conversationId, string newTitle);
    }
}
