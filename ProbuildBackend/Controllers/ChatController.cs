// ProbuildBackend/Controllers/ChatController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Services;
using ProbuildBackend.Models.DTO;
using System.Security.Claims;

namespace ProbuildBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly ChatService _chatService;
        private readonly ILogger<ChatController> _logger;

        public ChatController(ChatService chatService, ILogger<ChatController> logger)
        {
            _chatService = chatService;
            _logger = logger;
        }

        [HttpGet("my-prompts")]
        public async Task<IActionResult> GetMyPrompts()
        {
            _logger.LogInformation("DELETE ME: GetMyPrompts endpoint hit");
            var userId = User.FindFirstValue("UserId");
            if (userId == null)
            {
                _logger.LogWarning("DELETE ME: GetMyPrompts - UserId not found in token");
                return Unauthorized();
            }
            _logger.LogInformation($"DELETE ME: GetMyPrompts - Found UserId: {userId}");
            var prompts = await _chatService.GetAvailablePromptsAsync(userId);
            _logger.LogInformation($"DELETE ME: GetMyPrompts - Returning {prompts.Count} prompts");
            return Ok(prompts);
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartConversation([FromBody] StartConversationDto dto)
        {
            _logger.LogInformation("DELETE ME: StartConversation endpoint hit");
            var userId = User.FindFirstValue("UserId");
            if (userId == null)
            {
                _logger.LogWarning("DELETE ME: StartConversation - UserId not found in token");
                return Unauthorized();
            }
            _logger.LogInformation($"DELETE ME: StartConversation - Found UserId: {userId}");
            _logger.LogInformation($"DELETE ME: StartConversation - DTO: {System.Text.Json.JsonSerializer.Serialize(dto)}");

            var conversation = await _chatService.StartConversationAsync(userId, dto.UserType, dto.InitialMessage, dto.PromptKey, dto.BlueprintUrls);
            _logger.LogInformation($"DELETE ME: StartConversation - Returning new conversation with ID: {conversation.Id}");
            return Ok(conversation);
        }

        [HttpPost("{conversationId}/message")]
        public async Task<IActionResult> PostMessage(string conversationId, [FromBody] PostMessageDto dto)
        {
            _logger.LogInformation($"DELETE ME: PostMessage endpoint hit for conversationId: {conversationId}");
            var userId = User.FindFirstValue("UserId");
            if (userId == null)
            {
                _logger.LogWarning("DELETE ME: PostMessage - UserId not found in token");
                return Unauthorized();
            }
            _logger.LogInformation($"DELETE ME: PostMessage - Found UserId: {userId}");
            _logger.LogInformation($"DELETE ME: PostMessage - DTO: {System.Text.Json.JsonSerializer.Serialize(dto)}");

            var aiMessage = await _chatService.SendMessageAsync(conversationId, dto.Message, userId);
            _logger.LogInformation($"DELETE ME: PostMessage - Returning AI message");
            return Ok(aiMessage);
        }

        [HttpGet("{conversationId}")]
        public async Task<IActionResult> GetConversation(string conversationId)
        {
            _logger.LogInformation($"DELETE ME: GetConversation endpoint hit for conversationId: {conversationId}");
            var userId = User.FindFirstValue("UserId");
            if (userId == null)
            {
                _logger.LogWarning("DELETE ME: GetConversation - UserId not found in token");
                return Unauthorized();
            }
            _logger.LogInformation($"DELETE ME: GetConversation - Found UserId: {userId}");

            var history = await _chatService.GetConversationHistoryAsync(conversationId, userId);
            _logger.LogInformation($"DELETE ME: GetConversation - Returning conversation history with {history.Count} messages");
            return Ok(history);
        }

        [HttpGet("my-conversations")]
        public async Task<IActionResult> GetMyConversations()
        {
            _logger.LogInformation("DELETE ME: GetMyConversations endpoint hit");
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("DELETE ME: GetMyConversations - UserId not found in token");
                return Unauthorized();
            }
            _logger.LogInformation($"DELETE ME: GetMyConversations - Found UserId: {userId}");

            var conversations = await _chatService.GetUserConversationsAsync(userId);
            _logger.LogInformation($"DELETE ME: GetMyConversations - Returning {conversations.Count()} conversations");
            return Ok(conversations);
        }
    }

    public class StartConversationDto
    {
        public string InitialMessage { get; set; }
        public string PromptKey { get; set; }
        public List<string> BlueprintUrls { get; set; }
        public string UserType { get; set; }
    }

    public class PostMessageDto
    {
        public string Message { get; set; }
    }
}
