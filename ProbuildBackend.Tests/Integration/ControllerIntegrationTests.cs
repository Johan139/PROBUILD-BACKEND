using Xunit;
using Moq;
using ProbuildBackend.Services;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using ProbuildBackend.Middleware;
using ProbuildBackend.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProbuildBackend.Tests.Integration;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ProbuildBackend.Tests
{
    public class ControllerIntegrationTests : IntegrationTestBase
    {
        private readonly Mock<IConversationRepository> _mockConversationRepo;
        private readonly Mock<IAnalysisService> _mockAnalysisService;
        private readonly Mock<IAiService> _mockAiService;

        public ControllerIntegrationTests(CustomWebApplicationFactory<Program> factory) : base(factory)
        {
            _mockConversationRepo = factory.MockConversationRepo;
            _mockAnalysisService = factory.MockAnalysisService;
            _mockAiService = factory.MockAiService;
        }

        [Fact]
        public async Task StartConversation_WithPromptKeys_RoutesToAnalysisService()
        {
            // ARRANGE - Story 8
            var startDto = new StartConversationDto
            {
                InitialMessage = "",
                PromptKeys = new List<string> { "Carpentry_Subcontractor_Prompt.txt" },
                BlueprintUrls = new List<string> { "http://example.com/doc.pdf" }
            };

            var newConversationId = "conv-test-123";
            var analysisResult = "This is the carpentry analysis report.";

            _mockConversationRepo.Setup(r => r.CreateConversationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()))
                .ReturnsAsync(newConversationId);

            _mockAnalysisService.Setup(s => s.PerformAnalysisAsync(It.Is<AnalysisRequestDto>(dto =>
                dto.PromptKeys.Contains("Carpentry_Subcontractor_Prompt.txt")
            ))).ReturnsAsync(analysisResult);

            _mockConversationRepo.Setup(r => r.AddMessageAsync(It.Is<Message>(m =>
                m.ConversationId == newConversationId &&
                m.Role == "model" &&
                m.Content == analysisResult
            ))).Returns(Task.CompletedTask);

            var finalConversation = new Conversation { Id = newConversationId, UserId = "4929f316-4a97-4c9b-a671-8962532b6ab5" };
            _mockConversationRepo.Setup(r => r.GetConversationAsync(newConversationId)).ReturnsAsync(finalConversation);


            // ACT
            var token = GenerateJwtToken("4929f316-4a97-4c9b-a671-8962532b6ab5");
            Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await Client.PostAsJsonAsync("/api/chat/start", startDto);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<Conversation>();

            // ASSERT
            var returnedConversation = Assert.IsType<Conversation>(result);
            Assert.Equal(newConversationId, returnedConversation.Id);

            // Verify that a new conversation was created
            _mockConversationRepo.Verify(r => r.CreateConversationAsync("4929f316-4a97-4c9b-a671-8962532b6ab5", It.IsAny<string>(), startDto.BlueprintUrls), Times.Once);

            // Verify the analysis service was called
            _mockAnalysisService.Verify(s => s.PerformAnalysisAsync(It.Is<AnalysisRequestDto>(dto =>
                dto.PromptKeys.Count == 1 &&
                dto.PromptKeys[0] == "Carpentry_Subcontractor_Prompt.txt" &&
                dto.DocumentUrls.Count == 1
            )), Times.Once);

            // Verify the result was saved as the first message
            _mockConversationRepo.Verify(r => r.AddMessageAsync(It.Is<Message>(m => m.Content == analysisResult)), Times.Once);

            // Verify the general AI chat was NOT called
            _mockAiService.Verify(a => a.StartTextConversationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task StartConversation_AfterAnalysis_SeedsNewGeneralChat()
        {
            // ARRANGE - Story 4
            var reportContent = "This is a detailed analysis report that I want to discuss.";
            var startDto = new StartConversationDto
            {
                InitialMessage = $"Here is the report I want to discuss: {reportContent}",
                PromptKeys = new List<string>(), // No prompt keys, this is a general chat
                BlueprintUrls = new List<string>()
            };

            var newConversationId = "conv-discuss-456";
            var aiResponse = "Of course. What are your questions about the report?";

            _mockConversationRepo.Setup(r => r.CreateConversationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>()))
                .ReturnsAsync(newConversationId);

            // This time, we expect a call to the general-purpose AI service
            _mockAiService.Setup(s => s.StartTextConversationAsync(newConversationId, It.IsAny<string>(), startDto.InitialMessage))
                          .ReturnsAsync((aiResponse, newConversationId));

            _mockConversationRepo.Setup(r => r.AddMessageAsync(It.IsAny<Message>())).Returns(Task.CompletedTask);
            var finalConversation = new Conversation { Id = newConversationId, UserId = "4929f316-4a97-4c9b-a671-8962532b6ab5" };
            _mockConversationRepo.Setup(r => r.GetConversationAsync(newConversationId)).ReturnsAsync(finalConversation);

            // ACT
            var token = GenerateJwtToken("4929f316-4a97-4c9b-a671-8962532b6ab5");
            Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await Client.PostAsJsonAsync("/api/chat/start", startDto);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<Conversation>();

            // ASSERT
            var returnedConversation = Assert.IsType<Conversation>(result);
            Assert.Equal(newConversationId, returnedConversation.Id);

            // Verify AnalysisService was NOT called
            _mockAnalysisService.Verify(s => s.PerformAnalysisAsync(It.IsAny<AnalysisRequestDto>()), Times.Never);

            // Verify the general AI chat WAS called
            _mockAiService.Verify(a => a.StartTextConversationAsync(newConversationId, It.IsAny<string>(), startDto.InitialMessage), Times.Once);

            // Verify both user and model messages were saved
            _mockConversationRepo.Verify(r => r.AddMessageAsync(It.Is<Message>(m => m.Role == "user")), Times.Once);
            _mockConversationRepo.Verify(r => r.AddMessageAsync(It.Is<Message>(m => m.Role == "model")), Times.Once);
        }

        private string GenerateJwtToken(string userId)
        {
            var configuration = Factory.Services.GetRequiredService<IConfiguration>();
            var key = Encoding.UTF8.GetBytes(configuration["Jwt:Key"]);
            var issuer = configuration["Jwt:Issuer"];
            var audience = configuration["Jwt:Audience"];

            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = issuer,
                Audience = audience
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
