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

        public ChatController(ChatService chatService)
        {
            _chatService = chatService;
        }

        [HttpGet("my-prompts")]
        public async Task<IActionResult> GetMyPrompts()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return Unauthorized();
            }
            var prompts = await _chatService.GetAvailablePromptsAsync(userId);
            return Ok(prompts);
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartConversation([FromBody] StartConversationDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return Unauthorized();
            }

            var (aiMessage, conversationId) = await _chatService.StartConversationAsync(userId, dto.InitialMessage, dto.PromptKey, dto.BlueprintUrls);

            return Ok(new { aiMessage, conversationId });
        }

        [HttpPost("{conversationId}/message")]
        public async Task<IActionResult> PostMessage(string conversationId, [FromBody] PostMessageDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return Unauthorized();
            }

            var aiMessage = await _chatService.SendMessageAsync(conversationId, dto.Message, userId);
            return Ok(aiMessage);
        }

        [HttpGet("{conversationId}")]
        public async Task<IActionResult> GetConversation(string conversationId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return Unauthorized();
            }

            var history = await _chatService.GetConversationHistoryAsync(conversationId, userId);
            return Ok(history);
        }
    }

    public class StartConversationDto
    {
        public string InitialMessage { get; set; }
        public string PromptKey { get; set; }
        public List<string> BlueprintUrls { get; set; }
    }

    public class PostMessageDto
    {
        public string Message { get; set; }
    }
}