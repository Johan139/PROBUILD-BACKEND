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
        private readonly IAiAnalysisService _aiAnalysisService;

        public ChatService(
            IConversationRepository conversationRepository,
            IPromptManagerService promptManager,
            IAiService aiService,
            UserManager<UserModel> userManager,
            IWebHostEnvironment hostingEnvironment,
            AzureBlobService azureBlobService,
            IHubContext<ChatHub> hubContext,
            IAiAnalysisService aiAnalysisService)
        {
            _conversationRepository = conversationRepository;
            _promptManager = promptManager;
            _aiService = aiService;
            _userManager = userManager;
            _hostingEnvironment = hostingEnvironment;
            _promptMappings = LoadPromptMappings();
            _azureBlobService = azureBlobService;
            _hubContext = hubContext;
            _aiAnalysisService = aiAnalysisService;
        }

        private List<PromptMapping> LoadPromptMappings()
        {
            var filePath = Path.Combine(_hostingEnvironment.ContentRootPath, "Config", "prompt_mapping.json");
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<PromptMapping>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        // --- NEW HELPERS: dual-cast chunks to user AND group (no client changes required) ---
        private Task SendChunkAsync(string conversationId, string userId, string textWithSpace)
        {
            return Task.WhenAll(
                _hubContext.Clients.User(userId).SendAsync("ReceiveStreamChunk", conversationId, textWithSpace),
                _hubContext.Clients.Group(conversationId).SendAsync("ReceiveStreamChunk", conversationId, textWithSpace)
            );
        }

        private Task SendCompleteAsync(string conversationId, string userId)
        {
            return Task.WhenAll(
                _hubContext.Clients.User(userId).SendAsync("StreamComplete", conversationId),
                _hubContext.Clients.Group(conversationId).SendAsync("StreamComplete", conversationId)
            );
        }

        public async Task<List<object>> GetAvailablePromptsAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return new List<object>();
            }

            if (user.UserType == "GENERAL_CONTRACTOR")
            {
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

        public async Task<Conversation> StartConversationAsync(
            string userId,
            string userType,
            string initialMessage,
            List<string>? promptKeys = null,
            List<string>? blueprintUrls = null)
        {
            promptKeys ??= new List<string>();
            blueprintUrls ??= new List<string>();

            var title = promptKeys.Any()
                ? string.Join(", ", promptKeys)
                : (initialMessage.Length > 50 ? initialMessage[..50] : initialMessage);

            var conversationId = await _conversationRepository.CreateConversationAsync(userId, title, promptKeys);

            // Save the user's initial message (recommended so history looks right)
            await _conversationRepository.AddMessageAsync(new Message
            {
                ConversationId = conversationId,
                Role = "user",
                Content = initialMessage,
                Timestamp = DateTime.UtcNow
            });

            var systemPersonaPrompt = await _promptManager.GetPromptAsync(
                userType, promptKeys.FirstOrDefault() ?? "generic-prompt.txt");

            string aiResponse;

            // --- STREAMING PATHS ---
            if (!promptKeys.Any())
            {
                // Text-only: stream directly from your existing streaming source
                var sb = new System.Text.StringBuilder();
                await foreach (var chunk in _aiService.StreamTextResponseAsync(conversationId, initialMessage, new List<string>()))
                {
                    if (!string.IsNullOrWhiteSpace(chunk))
                    {
                        // Optional: break into words for �typewriter� feel (matches your SendMessageAsync UX)
                        foreach (var word in chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var textWithSpace = word + " ";
                            await SendChunkAsync(conversationId, userId, textWithSpace);
                            sb.Append(textWithSpace);
                            await Task.Delay(30); // pacing (same as SendMessageAsync)
                        }
                    }
                }

                await SendCompleteAsync(conversationId, userId);
                aiResponse = sb.ToString();
            }
            else
            {
                // Multimodal or selected analysis case:
                // If you have a true streaming API for analysis, call it here.
                // If not, simulate streaming so the UI behaves identically.
                var (fullResponse, _) = await _aiService.StartMultimodalConversationAsync(
                    conversationId, blueprintUrls, systemPersonaPrompt, initialMessage);

                var words = (fullResponse ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var sb = new System.Text.StringBuilder();

                foreach (var w in words)
                {
                    var textWithSpace = w + " ";
                    await SendChunkAsync(conversationId, userId, textWithSpace);
                    sb.Append(textWithSpace);
                    await Task.Delay(15); // a bit faster for precomputed text
                }

                await SendCompleteAsync(conversationId, userId);
                aiResponse = sb.ToString();
            }

            // Persist the AI message (so history contains the full response)
            var aiMessage = new Message
            {
                ConversationId = conversationId,
                Role = "model",
                Content = aiResponse,
                Timestamp = DateTime.UtcNow
            };
            await _conversationRepository.AddMessageAsync(aiMessage);

            // IMPORTANT: do NOT send a single ReceiveMessage here (it would duplicate a final message)
            // await _hubContext.Clients.Group(conversationId).SendAsync("ReceiveMessage", aiMessage);

            // Return the conversation (frontend can still use it for metadata; content is already streamed)
            var conversation = await _conversationRepository.GetConversationAsync(conversationId)
                                ?? throw new Exception("Failed to retrieve conversation after creation.");

            return conversation;
        }

        public async Task<Conversation> CreateConversationAsync(string userId, string title)
        {
            var conversationId = await _conversationRepository.CreateConversationAsync(userId, title, new List<string>());
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

            List<string> fileUrls = [];
            if (dto.Files != null && dto.Files.Count > 0)
            {
                var newUrls = await _azureBlobService.UploadFiles(dto.Files.ToList(), null, null);
                fileUrls.AddRange(newUrls);
            }

            if (dto.DocumentUrls != null && dto.DocumentUrls.Any())
            {
                fileUrls.AddRange(dto.DocumentUrls);
            }

            string aiResponse;
            string userMessageContent;
            if (string.IsNullOrEmpty(dto.Message))
            {
                var formattedPrompts = dto.PromptKeys?.Select(p => p.Replace(".txt", "").Replace("_", " ")) ?? Enumerable.Empty<string>();
                userMessageContent = $"Analysis started with selected prompts: {string.Join(", ", formattedPrompts)}";
            }
            else
            {
                userMessageContent = dto.Message;
            }

            if (!string.IsNullOrWhiteSpace(userMessageContent))
            {
                var userMessage = new Message
                {
                    ConversationId = conversationId,
                    Role = "user",
                    Content = userMessageContent,
                    Timestamp = DateTime.UtcNow
                };
                await _conversationRepository.AddMessageAsync(userMessage);
            }

            if (dto.PromptKeys != null && dto.PromptKeys.Any())
            {
                var analysisRequest = new AnalysisRequestDto
                {
                    AnalysisType = AnalysisType.Selected, // Or determine from context
                    PromptKeys = dto.PromptKeys,
                    DocumentUrls = fileUrls,
                    UserContext = dto.Message,
                    ConversationId = conversationId
                };
                aiResponse = await _aiAnalysisService.PerformSelectedAnalysisAsync(userId, analysisRequest, false, conversationId);
            }
             else if (dto.PromptKeys.Contains("SYSTEM_RENOVATION_ANALYSIS"))
             {
                var jobDetails = new JobModel { UserId = userId };
                aiResponse = await _aiAnalysisService.PerformRenovationAnalysisAsync(userId, fileUrls, jobDetails, false, dto.Message, "");
             }
             else
             {
                var sb = new System.Text.StringBuilder();

                await foreach (var chunk in _aiService.StreamTextResponseAsync(conversationId, dto.Message, fileUrls))
                {
                    if (!string.IsNullOrWhiteSpace(chunk))
                    {
                        var words = chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var word in words)
                        {
                            var textWithSpace = word + " ";
                            await SendChunkAsync(conversationId, userId, textWithSpace);
                            sb.Append(textWithSpace);
                            await Task.Delay(30); // optional for pacing
                        }
                    }
                }

                await SendCompleteAsync(conversationId, userId);
                aiResponse = sb.ToString();
            }

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

            // Keep this disabled to avoid double-rendering against the stream
            // await _hubContext.Clients.Group(conversationId).SendAsync("ReceiveMessage", frontendMessage);

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
