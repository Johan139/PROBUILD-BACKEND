using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Services;
using ProbuildBackend.Models.DTO;
using System.Security.Claims;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models.Enums;
using System.Text.RegularExpressions;

namespace ProbuildBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly ChatService _chatService;
        private readonly ILogger<ChatController> _logger;
        private readonly AzureBlobService _azureBlobService;
        private readonly ApplicationDbContext _context;

        public ChatController(ChatService chatService, ILogger<ChatController> logger, AzureBlobService azureBlobService, ApplicationDbContext context)
        {
            _chatService = chatService;
            _logger = logger;
            _azureBlobService = azureBlobService;
            _context = context;
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

       [AllowAnonymous]
       [HttpGet("prompts")]
       public IActionResult GetPrompts()
       {
           var filePath = Path.Combine(Directory.GetCurrentDirectory(), "Config", "prompt_mapping.json");
           if (!System.IO.File.Exists(filePath))
           {
               return NotFound("Prompt mapping file not found.");
           }

           var json = System.IO.File.ReadAllText(filePath);
           return Content(json, "application/json");
       }

        [HttpPost("start")]
        public async Task<IActionResult> StartConversation([FromForm] StartConversationDto dto)
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

            var conversation = await _chatService.StartConversationAsync(userId, dto.UserType, dto.InitialMessage, dto.PromptKeys, dto.BlueprintUrls);
            _logger.LogInformation($"DELETE ME: StartConversation - Returning new conversation with ID: {conversation.Id}");
            return Ok(conversation);
        }

        [HttpPost("{conversationId}/message")]
        public async Task<IActionResult> PostMessage(string conversationId, [FromForm] PostMessageDto dto)
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

            var aiMessage = await _chatService.SendMessageAsync(conversationId, userId, dto);
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


        [HttpPost("{conversationId}/upload")]
        public async Task<IActionResult> UploadChatFile(string conversationId, IFormFileCollection files)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var uploadedFileUrls = await _azureBlobService.UploadFiles(files.ToList(), null, null);

            foreach (var (file, url) in files.Zip(uploadedFileUrls, (f, u) => (f, u)))
            {
                var jobDocument = new JobDocumentModel
                {
                    JobId = null,
                    ConversationId = conversationId,
                    FileName = file.FileName,
                    BlobUrl = url,
                    SessionId = null,
                    UploadedAt = DateTime.UtcNow,
                    Size = file.Length
                };
                _context.JobDocuments.Add(jobDocument);
            }

            await _context.SaveChangesAsync();

            return Ok(new { fileUrls = uploadedFileUrls });
        }

        [HttpGet("{conversationId}/documents")]
        public async Task<IActionResult> GetConversationDocuments(string conversationId)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var documents = await _context.JobDocuments
                .Where(d => d.ConversationId == conversationId)
                .ToListAsync();

            return Ok(documents);
        }

        [HttpPut("conversation/title")]
        public async Task<IActionResult> UpdateConversationTitle([FromBody] UpdateConversationTitleDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.NewTitle))
            {
                return BadRequest("Title cannot be empty.");
            }

            var sanitizedTitle = Regex.Replace(dto.NewTitle, @"[^a-zA-Z0-9\s]", "").Trim();

            if (sanitizedTitle.Length > 50)
            {
                sanitizedTitle = sanitizedTitle.Substring(0, 50);
            }

            if (string.IsNullOrWhiteSpace(sanitizedTitle))
            {
                return BadRequest("Sanitized title cannot be empty.");
            }

            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var conversation = await _context.Conversations.FindAsync(dto.ConversationId);
            if (conversation == null || conversation.UserId != userId)
            {
                return NotFound();
            }

            await _chatService.UpdateConversationTitleAsync(dto.ConversationId, sanitizedTitle);
            return Ok();
        }
    }

}
