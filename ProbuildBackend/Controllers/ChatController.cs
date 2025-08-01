using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Services;
using ProbuildBackend.Models.DTO;
using System.Security.Claims;
using ProbuildBackend.Interface;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using ProbuildBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace ProbuildBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly ChatService _chatService;
        private readonly ILogger<ChatController> _logger;
        private readonly IComparisonAnalysisService _comparisonAnalysisService;
        private readonly IRenovationAnalysisService _renovationAnalysisService;
        private readonly AzureBlobService _azureBlobService;
        private readonly ApplicationDbContext _context;

        public ChatController(ChatService chatService, ILogger<ChatController> logger, IComparisonAnalysisService comparisonAnalysisService, IRenovationAnalysisService renovationAnalysisService, AzureBlobService azureBlobService, ApplicationDbContext context)
        {
            _chatService = chatService;
            _logger = logger;
            _comparisonAnalysisService = comparisonAnalysisService;
            _renovationAnalysisService = renovationAnalysisService;
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

        [HttpPost("start-renovation-analysis")]
        public async Task<IActionResult> StartRenovationAnalysis(IFormFileCollection files)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var request = new RenovationAnalysisRequest { UserId = userId };
            var result = await _renovationAnalysisService.PerformAnalysisAsync(request, files.ToList());
            return Ok(result);
        }

        [HttpPost("start-subcontractor-comparison")]
        public async Task<IActionResult> StartSubcontractorComparison(IFormFileCollection files)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var request = new ComparisonAnalysisRequest
            {
                UserId = userId,
                ComparisonType = ComparisonType.Subcontractor
            };
            var result = await _comparisonAnalysisService.PerformAnalysisAsync(request, files.ToList());
            return Ok(result);
        }

        [HttpPost("start-vendor-comparison")]
        public async Task<IActionResult> StartVendorComparison(IFormFileCollection files)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var request = new ComparisonAnalysisRequest
            {
                UserId = userId,
                ComparisonType = ComparisonType.Vendor
            };
            var result = await _comparisonAnalysisService.PerformAnalysisAsync(request, files.ToList());
            return Ok(result);
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
            if (dto.NewTitle.Length > 255)
            {
                return BadRequest("Title cannot be longer than 255 characters.");
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

            await _chatService.UpdateConversationTitleAsync(dto.ConversationId, dto.NewTitle);
            return Ok();
        }
    }

    public class UpdateConversationTitleDto
    {
        public string ConversationId { get; set; }
        public string NewTitle { get; set; }
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
