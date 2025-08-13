using ProbuildBackend.Interface;
using Microsoft.AspNetCore.Identity;
using System.Text.Json;
using ProbuildBackend.Models;
using Microsoft.AspNetCore.SignalR;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Models.Enums;

namespace ProbuildBackend.Services
{
    public class ChatService
    {
        private readonly IConversationRepository _conversationRepository;
        private readonly IPromptManagerService _promptManager;
        private readonly IAiService _aiService;
        private readonly UserManager<UserModel> _userManager;
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly List<PromptMapping> _promptMappings;
        private readonly AzureBlobService _azureBlobService;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IAnalysisService _analysisService;


        public ChatService(
            IConversationRepository conversationRepository,
            IPromptManagerService promptManager,
            IAiService aiService,
            UserManager<UserModel> userManager,
            IWebHostEnvironment hostingEnvironment,
            AzureBlobService azureBlobService,
            IHubContext<ChatHub> hubContext,
            IAnalysisService analysisService)
        {
            _conversationRepository = conversationRepository;
            _promptManager = promptManager;
            _aiService = aiService;
            _userManager = userManager;
            _hostingEnvironment = hostingEnvironment;
            _promptMappings = LoadPromptMappings();
            _azureBlobService = azureBlobService;
            _hubContext = hubContext;
            _analysisService = analysisService;
        }

        private List<PromptMapping> LoadPromptMappings()
        {
            var filePath = Path.Combine(_hostingEnvironment.ContentRootPath, "Config", "prompt_mapping.json");
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<PromptMapping>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }


        public async Task<List<object>> GetAvailablePromptsAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            Console.WriteLine($"DELETE ME: [ChatService] Checking prompts for userId: {userId}");
            if (user == null)
            {
                Console.WriteLine($"DELETE ME: [ChatService] User with ID {userId} not found.");
                return new List<object>();
            }
            Console.WriteLine($"DELETE ME: [ChatService] Found user with UserType: {user.UserType}");

            if (user.UserType == "GENERAL_CONTRACTOR")
            {
                Console.WriteLine("DELETE ME: [ChatService] User is GENERAL_CONTRACTOR, returning all prompts.");
                return _promptMappings
                    .Select(p => new { promptName = p.TradeName.Replace("_", " "), promptKey = p.PromptFileName })
                    .GroupBy(p => p.promptKey)
                    .Select(g => g.First())
                    .Cast<object>()
                    .ToList();
            }

            var trade = user.Trade;
            if (string.IsNullOrEmpty(trade))
            {
                return new List<object>();
            }

            var prompts = _promptMappings
                .Where(p => p.TradeName.Equals(trade, System.StringComparison.OrdinalIgnoreCase))
                .Select(p => new { promptName = $"{trade} Analysis", promptKey = p.PromptFileName })
                .Cast<object>()
                .ToList();

            return prompts;
        }

        public async Task<Conversation> StartConversationAsync(string userId, string userType, string initialMessage, List<string>? promptKeys = null, List<string>? blueprintUrls = null)
        {
            promptKeys ??= new List<string>();
            blueprintUrls ??= new List<string>();

            var title = promptKeys.Any()
                ? string.Join(", ", promptKeys)
                : (initialMessage.Length > 50 ? initialMessage.Substring(0, 50) : initialMessage);

            var conversationId = await _conversationRepository.CreateConversationAsync(userId, title, promptKeys);

            var systemPersonaPrompt = await _promptManager.GetPromptAsync(userType, promptKeys.FirstOrDefault() ?? "generic-chat");

            string initialResponse;

            if (!promptKeys.Any())
            {
                (initialResponse, _) = await _aiService.StartTextConversationAsync(conversationId, systemPersonaPrompt, initialMessage);
            }
            else
            {
                (initialResponse, _) = await _aiService.StartMultimodalConversationAsync(conversationId, blueprintUrls, systemPersonaPrompt, initialMessage);
            }

            var userMessage = new Message
            {
                ConversationId = conversationId,
                Role = "user",
                Content = initialMessage,
                Timestamp = DateTime.UtcNow
            };
            await _conversationRepository.AddMessageAsync(userMessage);

            var aiMessage = new Message
            {
                ConversationId = conversationId,
                Role = "model",
                Content = initialResponse,
                Timestamp = DateTime.UtcNow
            };
            await _conversationRepository.AddMessageAsync(aiMessage);

            await _hubContext.Clients.Group(conversationId).SendAsync("ReceiveMessage", aiMessage);

            var conversation = await _conversationRepository.GetConversationAsync(conversationId) ?? throw new Exception("Failed to retrieve conversation after creation.");

            return conversation;
        }

        public async Task<Message> SendMessageAsync(string conversationId, string userId, PostMessageDto dto)
        {
            var conversation = await _conversationRepository.GetConversationAsync(conversationId);
            if (conversation == null || conversation.UserId != userId)
            {
                throw new UnauthorizedAccessException("User is not authorized to access this conversation.");
            }

            List<string>? fileUrls = null;
            if (dto.Files != null && dto.Files.Count > 0)
            {
                fileUrls = await _azureBlobService.UploadFiles(dto.Files.ToList(), null, null);
            }

            string aiResponse;

            if (dto.PromptKeys != null && dto.PromptKeys.Any())
            {
                var analysisRequest = new AnalysisRequestDto
                {
                    AnalysisType = AnalysisType.Selected, // Or determine from context
                    PromptKeys = dto.PromptKeys,
                    DocumentUrls = fileUrls ?? new List<string>(),
                    UserContext = dto.Message
                };
                aiResponse = await _analysisService.PerformAnalysisAsync(analysisRequest);
            }
            else
            {
                var (continueResponse, _) = await _aiService.ContinueConversationAsync(conversationId, userId, dto.Message, fileUrls);
                aiResponse = continueResponse;
            }

            var userMessage = new Message
            {
                ConversationId = conversationId,
                Role = "user",
                Content = dto.Message,
                Timestamp = DateTime.UtcNow
            };
            await _conversationRepository.AddMessageAsync(userMessage);

            var aiMessage = new Message
            {
                ConversationId = conversationId,
                Role = "model",
                Content = aiResponse,
                Timestamp = DateTime.UtcNow
            };
            await _conversationRepository.AddMessageAsync(aiMessage);

            var frontendMessage = new SignalRMessage
            {
                Id = aiMessage.Id,
                ConversationId = aiMessage.ConversationId,
                Role = aiMessage.Role,
                Content = aiMessage.Content,
                IsSummarized = aiMessage.IsSummarized,
                Timestamp = aiMessage.Timestamp
            };

            await _hubContext.Clients.Group(conversationId).SendAsync("ReceiveMessage", frontendMessage);

            return aiMessage;
        }

        public async Task<List<Message>> GetConversationHistoryAsync(string conversationId, string userId)
        {
            var conversation = await _conversationRepository.GetConversationAsync(conversationId);
            if (conversation == null || conversation.UserId != userId)
            {
                throw new UnauthorizedAccessException("User is not authorized to access this conversation.");
            }
            return await _conversationRepository.GetMessagesAsync(conversationId);
        }

        public async Task<IEnumerable<Conversation>> GetUserConversationsAsync(string userId)
        {
          return await _conversationRepository.GetByUserIdAsync(userId);
        }

        public async Task UpdateConversationTitleAsync(string conversationId, string newTitle)
        {
            await _conversationRepository.UpdateConversationTitleAsync(conversationId, newTitle);
        }
    }
}
