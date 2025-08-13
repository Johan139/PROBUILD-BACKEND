using Moq;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Services;
using Xunit;
using Microsoft.Extensions.Logging;

namespace ProbuildBackend.Tests.Services
{
    public class ComprehensiveAnalysisServiceTests
    {
        private readonly Mock<IAiService> _aiServiceMock;
        private readonly Mock<IPromptManagerService> _promptManagerMock;
        private readonly Mock<ILogger<ComprehensiveAnalysisService>> _loggerMock;
        private readonly ComprehensiveAnalysisService _service;

        public ComprehensiveAnalysisServiceTests()
        {
            _aiServiceMock = new Mock<IAiService>();
            _promptManagerMock = new Mock<IPromptManagerService>();
            _loggerMock = new Mock<ILogger<ComprehensiveAnalysisService>>();
            _service = new ComprehensiveAnalysisService(_aiServiceMock.Object, _promptManagerMock.Object, _loggerMock.Object);
        }

        private JobModel CreateTestJobModel()
        {
            return new JobModel
            {
                ProjectName = "Test Project",
                JobType = "Renovation",
                Address = "123 Test St",
                OperatingArea = "Testville",
                DesiredStartDate = System.DateTime.Now,
                Stories = 2,
                BuildingSize = 2000,
                WallStructure = "Wood",
                WallInsulation = "Fiberglass",
                RoofStructure = "Truss",
                RoofInsulation = "Spray Foam",
                Foundation = "Slab",
                Finishes = "Standard",
                ElectricalSupplyNeeds = "200 Amp"
            };
        }

        [Fact]
        public async Task PerformAnalysisFromFilesAsync_FetchesPromptsAndStartsConversation()
        {
            // Arrange
            var jobDetails = CreateTestJobModel();
            var documentUris = new List<string> { "http://fake.url/doc1.pdf" };
            _promptManagerMock.Setup(p => p.GetPromptAsync("", "system-persona.txt")).ReturnsAsync("System Persona");
            _promptManagerMock.Setup(p => p.GetPromptAsync("", "prompt-00-initial-analysis.txt")).ReturnsAsync("Initial Prompt");
            _aiServiceMock.Setup(a => a.StartMultimodalConversationAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(("Initial AI Response", "conv123"));

            // Act
            await _service.PerformAnalysisFromFilesAsync("4929f316-4a97-4c9b-a671-8962532b6ab5", documentUris, jobDetails);

            // Assert
            _promptManagerMock.Verify(p => p.GetPromptAsync("", "system-persona.txt"), Times.Once);
            _promptManagerMock.Verify(p => p.GetPromptAsync("", "prompt-00-initial-analysis.txt"), Times.Once);
            _aiServiceMock.Verify(a => a.StartMultimodalConversationAsync("4929f316-4a97-4c9b-a671-8962532b6ab5", documentUris, "System Persona", It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task PerformAnalysisFromFilesAsync_HandlesBlueprintFailure()
        {
            // Arrange
            var jobDetails = CreateTestJobModel();
            var documentUris = new List<string> { "http://fake.url/doc1.pdf" };
            _promptManagerMock.Setup(p => p.GetPromptAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("Some prompt");
            _aiServiceMock.Setup(a => a.StartMultimodalConversationAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(("BLUEPRINT FAILURE", "conv123"));
            _promptManagerMock.Setup(p => p.GetPromptAsync(It.IsAny<string>(), "prompt-failure-corrective-action.txt")).ReturnsAsync("Corrective Prompt");
            _aiServiceMock.Setup(a => a.PerformMultimodalAnalysisAsync(documentUris, It.IsAny<string>())).ReturnsAsync("Corrective Action Report");

            // Act
            var result = await _service.PerformAnalysisFromFilesAsync("4929f316-4a97-4c9b-a671-8962532b6ab5", documentUris, jobDetails);

            // Assert
            Assert.Equal("Corrective Action Report", result);
            _aiServiceMock.Verify(a => a.PerformMultimodalAnalysisAsync(documentUris, "Corrective Prompt\n\nOriginal Failed Response:\nBLUEPRINT FAILURE"), Times.Once);
        }
    }
}
