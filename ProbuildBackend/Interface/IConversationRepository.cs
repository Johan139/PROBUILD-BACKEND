// ProbuildBackend/Interface/IConversationRepository.cs
using ProbuildBackend.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IConversationRepository
{
    Task<Conversation?> GetConversationAsync(string conversationId);
    Task<string> CreateConversationAsync(string userId, string title);
    Task<List<Message>> GetMessagesAsync(string conversationId, bool includeSummarized = true);
    Task<List<Message>> GetUnsummarizedMessagesAsync(string conversationId);
    Task AddMessageAsync(Message message);
    Task UpdateConversationSummaryAsync(string conversationId, string? newSummary);
    Task MarkMessagesAsSummarizedAsync(IEnumerable<long> messageIds);
}