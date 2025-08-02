using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using ProbuildBackend.Interface;
using System.Threading.Tasks;

namespace ProbuildBackend.Middleware
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IConversationRepository _conversationRepository;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(IConversationRepository conversationRepository, ILogger<ChatHub> logger)
        {
            _conversationRepository = conversationRepository;
            _logger = logger;
            _logger.LogInformation("ChatHub instance created.");
        }

        public override Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            _logger.LogError(exception, "Client disconnected: {ConnectionId}", Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        public async Task JoinConversationGroup(string conversationId)
        {
            _logger.LogInformation("JoinConversationGroup called for conversation {ConversationId} by connection {ConnectionId}", conversationId, Context.ConnectionId);
            
            // Get the actual user ID from the claims
            var userId = Context.User?.FindFirst("UserId")?.Value ?? 
                        Context.User?.FindFirst("userId")?.Value;
            
            _logger.LogInformation("Extracted User ID from claims: {UserId}", userId);
            _logger.LogInformation("Context.UserIdentifier (for comparison): {UserIdentifier}", Context.UserIdentifier);
            
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Unable to extract user ID from claims for conversation {ConversationId}", conversationId);
                return;
            }
            
            var conversation = await _conversationRepository.GetConversationAsync(conversationId);
            if (conversation == null)
            {
                _logger.LogWarning("Attempt to join a non-existent conversation {ConversationId}", conversationId);
                return;
            }
            
            if (conversation.UserId != userId)
            {
                _logger.LogWarning("User {UserId} attempted to join conversation {ConversationId} without permission. Expected: {ExpectedUserId}", userId, conversationId, conversation.UserId);
                return;
            }
            
            await Groups.AddToGroupAsync(Context.ConnectionId, conversationId);
            _logger.LogInformation("User {UserId} successfully joined conversation group {ConversationId}", userId, conversationId);
        }
    }
}
