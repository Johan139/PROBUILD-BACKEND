using Xunit;
using Moq;
using ProbuildBackend.Services;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using ProbuildBackend.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;

namespace ProbuildBackend.Tests.Services
{
    public class ChatServiceTests
    {
        private readonly Mock<IConversationRepository> _mockConversationRepo;
        private readonly Mock<IPromptManagerService> _mockPromptManager;
        private readonly Mock<IAiService> _mockAiService;
        private readonly Mock<UserManager<UserModel>> _mockUserManager;
        private readonly Mock<IWebHostEnvironment> _mockHostingEnvironment;
        private readonly Mock<AzureBlobService> _mockAzureBlobService;
        private readonly Mock<IHubContext<ChatHub>> _mockHubContext;
        private readonly Mock<IAnalysisService> _mockAnalysisService;
        private readonly ChatService _chatService;

        public ChatServiceTests()
        {
            _mockConversationRepo = new Mock<IConversationRepository>();
            _mockPromptManager = new Mock<IPromptManagerService>();
            _mockAiService = new Mock<IAiService>();

            var userStoreMock = new Mock<IUserStore<UserModel>>();
            _mockUserManager = new Mock<UserManager<UserModel>>(
                userStoreMock.Object,
                new Mock<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Object,
                new Mock<IPasswordHasher<UserModel>>().Object,
                new IUserValidator<UserModel>[0],
                new IPasswordValidator<UserModel>[0],
                new Mock<ILookupNormalizer>().Object,
                new Mock<IdentityErrorDescriber>().Object,
                new Mock<IServiceProvider>().Object,
                new Mock<ILogger<UserManager<UserModel>>>().Object);

            _mockHostingEnvironment = new Mock<IWebHostEnvironment>();
            // Build real configuration
            var basePath = AppContext.BaseDirectory;
            var projectRoot = Path.GetFullPath(Path.Combine(basePath, "..", "..", "..", "..", "ProbuildBackend"));
            var appSettingsPath = Path.Combine(projectRoot, "appsettings.json");
            var configuration = new ConfigurationBuilder().AddJsonFile(appSettingsPath).Build();

            var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
            var mockLogger = new Mock<ILogger<AzureBlobService>>();
            _mockAzureBlobService = new Mock<AzureBlobService>(() => new AzureBlobService(configuration, mockHttpContextAccessor.Object, mockLogger.Object));
            _mockHubContext = new Mock<IHubContext<ChatHub>>();
            _mockAnalysisService = new Mock<IAnalysisService>();

            // Mocking the HubContext setup
            var mockClients = new Mock<IHubClients>();
            var mockClientProxy = new Mock<IClientProxy>();
            mockClients.Setup(clients => clients.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
            _mockHubContext.Setup(x => x.Clients).Returns(mockClients.Object);

            // Mocking file system access for prompt mappings
            _mockHostingEnvironment.Setup(env => env.ContentRootPath).Returns(Directory.GetCurrentDirectory());
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Config");
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            var promptFilePath = Path.Combine(configPath, "prompt_mapping.json");
            File.WriteAllText(promptFilePath, "[]");


            // Setup ContentRootPath to avoid ArgumentNullException in Path.Combine
            _mockHostingEnvironment.Setup(e => e.ContentRootPath).Returns(projectRoot);

            _chatService = new ChatService(
                _mockConversationRepo.Object,
                _mockPromptManager.Object,
                _mockAiService.Object,
                _mockUserManager.Object,
                _mockHostingEnvironment.Object,
                _mockAzureBlobService.Object,
                _mockHubContext.Object,
                _mockAnalysisService.Object
            );
        }

        // Tests will be added here
        [Fact]
        public async Task PostMessageAsync_WithPromptKeys_ShouldCallAnalysisService()
        {
            // Arrange
            var conversationId = "conv123";
            var userId = "4929f316-4a97-4c9b-a671-8962532b6ab5";
            var dto = new PostMessageDto { Message = "Analyze this", PromptKeys = new List<string> { "Fire_Protection_Subcontractor_Prompt.txt" } };
            var conversation = new Conversation { Id = conversationId, UserId = userId };
            var expectedResponse = "Analysis complete.";

            _mockConversationRepo.Setup(r => r.GetConversationAsync(conversationId)).ReturnsAsync(conversation);
            _mockAnalysisService.Setup(s => s.PerformAnalysisAsync(It.IsAny<AnalysisRequestDto>()))
                                .ReturnsAsync(expectedResponse);
            _mockConversationRepo.Setup(r => r.AddMessageAsync(It.IsAny<Message>())).Returns(Task.CompletedTask);

            // Act
            var result = await _chatService.SendMessageAsync(conversationId, userId, dto);

            // Assert
            _mockAnalysisService.Verify(s => s.PerformAnalysisAsync(It.Is<AnalysisRequestDto>(req =>
                req.PromptKeys.Contains("Fire_Protection_Subcontractor_Prompt.txt") &&
                req.UserContext == "Analyze this"
            )), Times.Once);
            _mockAiService.Verify(s => s.ContinueConversationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()), Times.Never);
            Assert.Equal(expectedResponse, result.Content);
        }

        [Fact]
        public async Task PostMessageAsync_WithoutPromptKeys_ShouldCallAiService()
        {
            // Arrange
            var conversationId = "conv123";
            var userId = "4929f316-4a97-4c9b-a671-8962532b6ab5";
            var dto = new PostMessageDto { Message = "Hello there" };
            var conversation = new Conversation { Id = conversationId, UserId = userId };
            var expectedResponse = "General Kenobi!";

            _mockConversationRepo.Setup(r => r.GetConversationAsync(conversationId)).ReturnsAsync(conversation);
            _mockAiService.Setup(s => s.ContinueConversationAsync(conversationId, userId, dto.Message, null))
                          .ReturnsAsync((expectedResponse, "conv123"));
            _mockConversationRepo.Setup(r => r.AddMessageAsync(It.IsAny<Message>())).Returns(Task.CompletedTask);

            // Act
            var result = await _chatService.SendMessageAsync(conversationId, userId, dto);

            // Assert
            _mockAnalysisService.Verify(s => s.PerformAnalysisAsync(It.IsAny<AnalysisRequestDto>()), Times.Never);
            _mockAiService.Verify(s => s.ContinueConversationAsync(conversationId, userId, dto.Message, null), Times.Once);
            Assert.Equal(expectedResponse, result.Content);
        }

        [Fact]
        public async Task StartConversationAsync_ShouldCreateConversationAndAddMessages()
        {
            // Arrange
            var userId = "4929f316-4a97-4c9b-a671-8962532b6ab5";
            var userType = "GENERAL_CONTRACTOR";
            var initialMessage = "Let's start a new chat.";
            var newConversationId = "newConv456";
            var aiResponse = "Hello! How can I help you?";

            _mockConversationRepo.Setup(r => r.CreateConversationAsync(userId, It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(newConversationId);
            _mockPromptManager.Setup(p => p.GetPromptAsync(userType, "generic-chat")).ReturnsAsync("System prompt");
            _mockAiService.Setup(s => s.StartTextConversationAsync(newConversationId, "System prompt", initialMessage))
                          .ReturnsAsync((aiResponse, newConversationId));
            _mockConversationRepo.Setup(r => r.AddMessageAsync(It.IsAny<Message>())).Returns(Task.CompletedTask);
            _mockConversationRepo.Setup(r => r.GetConversationAsync(newConversationId)).ReturnsAsync(new Conversation { Id = newConversationId, UserId = userId });

            // Act
            var result = await _chatService.StartConversationAsync(userId, userType, initialMessage);

            // Assert
            _mockConversationRepo.Verify(r => r.CreateConversationAsync(userId, It.Is<string>(t => t.Contains("Let's start")), It.Is<List<string>>(l => !l.Any())), Times.Once);
            _mockConversationRepo.Verify(r => r.AddMessageAsync(It.Is<Message>(m => m.Role == "user" && m.Content == initialMessage)), Times.Once);
            _mockConversationRepo.Verify(r => r.AddMessageAsync(It.Is<Message>(m => m.Role == "model" && m.Content == aiResponse)), Times.Once);
            Assert.Equal(newConversationId, result.Id);
        }
    }
}
