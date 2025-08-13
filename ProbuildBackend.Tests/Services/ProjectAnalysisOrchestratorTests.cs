using Moq;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using Xunit;
using Microsoft.Extensions.Logging;

namespace ProbuildBackend.Tests.Services
{
    public class ProjectAnalysisOrchestratorTests
    {
        private readonly Mock<IAiService> _aiServiceMock;
        private readonly Mock<IPromptManagerService> _promptManagerMock;
        private readonly Mock<ILogger<ProjectAnalysisOrchestrator>> _loggerMock;
        private readonly Mock<IComprehensiveAnalysisService> _comprehensiveAnalysisServiceMock;
        private readonly ProjectAnalysisOrchestrator _orchestrator;

        public ProjectAnalysisOrchestratorTests()
        {
            _aiServiceMock = new Mock<IAiService>();
            _promptManagerMock = new Mock<IPromptManagerService>();
            _loggerMock = new Mock<ILogger<ProjectAnalysisOrchestrator>>();
            _comprehensiveAnalysisServiceMock = new Mock<IComprehensiveAnalysisService>();
            _orchestrator = new ProjectAnalysisOrchestrator(_aiServiceMock.Object, _promptManagerMock.Object, _loggerMock.Object, _comprehensiveAnalysisServiceMock.Object);
        }

        [Fact]
        public async Task StartFullAnalysisAsync_DelegatesToComprehensiveService()
        {
            // Arrange
            var jobDetails = new JobModel();
            var images = new List<byte[]> { new byte[0] };
            _comprehensiveAnalysisServiceMock.Setup(s => s.PerformAnalysisFromImagesAsync("4929f316-4a97-4c9b-a671-8962532b6ab5", images, jobDetails))
                .ReturnsAsync("Comprehensive Analysis Result");

            // Act
            var result = await _orchestrator.StartFullAnalysisAsync("4929f316-4a97-4c9b-a671-8962532b6ab5", images, jobDetails);

            // Assert
            Assert.Equal("Comprehensive Analysis Result", result);
            _comprehensiveAnalysisServiceMock.Verify(s => s.PerformAnalysisFromImagesAsync("4929f316-4a97-4c9b-a671-8962532b6ab5", images, jobDetails), Times.Once);
        }

        [Fact]
        public async Task GenerateRebuttalAsync_CallsAiServiceWithCorrectPrompt()
        {
            // Arrange
            _promptManagerMock.Setup(p => p.GetPromptAsync("", "prompt-22-rebuttal")).ReturnsAsync("Rebuttal Prompt");
            _aiServiceMock.Setup(a => a.ContinueConversationAsync("conv123", "system-user", It.IsAny<string>(), null))
                .ReturnsAsync(("Rebuttal Response", "conv123"));

            // Act
            var result = await _orchestrator.GenerateRebuttalAsync("conv123", "Client Query");

            // Assert
            Assert.Equal("Rebuttal Response", result);
            _aiServiceMock.Verify(a => a.ContinueConversationAsync("conv123", "system-user", "Rebuttal Prompt\n\n**Client Query to Address:**\nClient Query", null), Times.Once);
        }
    }
}
