using Xunit;
using Moq;
using ProbuildBackend.Services;
using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Models.Enums;
using Microsoft.Extensions.Logging;

namespace ProbuildBackend.Tests.Services
{
    public class AnalysisServiceTests
    {
        private readonly Mock<ILogger<AnalysisService>> _mockLogger;
        private readonly Mock<IPromptManagerService> _mockPromptManager;
        private readonly Mock<IAiService> _mockAiService;
        private readonly AnalysisService _analysisService;

        // Using constants defined in the service to ensure tests stay in sync.
        private const string SelectedAnalysisPersonaKey = "sub-contractor-selected-prompt-master-prompt.txt";
        private const string RenovationAnalysisPersonaKey = "ProBuildAI_Renovation_Prompt.txt";
        private const string FailureCorrectiveActionKey = "prompt-failure-corrective-action.txt";

        public AnalysisServiceTests()
        {
            _mockLogger = new Mock<ILogger<AnalysisService>>();
            _mockPromptManager = new Mock<IPromptManagerService>();
            _mockAiService = new Mock<IAiService>();
            _analysisService = new AnalysisService(_mockLogger.Object, _mockPromptManager.Object, _mockAiService.Object);
        }

        [Fact]
        public async Task PerformAnalysisAsync_SinglePromptKey_ConstructsCorrectPrompt()
        {
            // Arrange
            var requestDto = new AnalysisRequestDto
            {
                AnalysisType = AnalysisType.Selected,
                PromptKeys = new List<string> { "prompt1.txt" },
                DocumentUrls = new List<string> { "http://example.com/doc1.pdf" }
            };

            var personaPrompt = "This is the master persona.";
            var subPrompt1 = "This is the first sub-prompt.";
            var expectedFinalPrompt = $"{personaPrompt}\n\n{subPrompt1}";
            var expectedResult = "Successful analysis.";

            _mockPromptManager.Setup(p => p.GetPromptAsync(It.IsAny<string>(), SelectedAnalysisPersonaKey)).ReturnsAsync(personaPrompt);
            _mockPromptManager.Setup(p => p.GetPromptAsync(It.IsAny<string>(), "prompt1.txt")).ReturnsAsync(subPrompt1);
            _mockAiService.Setup(a => a.PerformMultimodalAnalysisAsync(requestDto.DocumentUrls, expectedFinalPrompt)).ReturnsAsync(expectedResult);

            // Act
            var result = await _analysisService.PerformAnalysisAsync(requestDto);

            // Assert
            Assert.Equal(expectedResult, result);
            _mockPromptManager.Verify(p => p.GetPromptAsync(It.IsAny<string>(), SelectedAnalysisPersonaKey), Times.Once);
            _mockPromptManager.Verify(p => p.GetPromptAsync(It.IsAny<string>(), "prompt1.txt"), Times.Once);
            _mockAiService.Verify(a => a.PerformMultimodalAnalysisAsync(requestDto.DocumentUrls, expectedFinalPrompt), Times.Once);
        }

        [Fact]
        public async Task PerformAnalysisAsync_MultiPromptKeys_AggregatesPromptsCorrectly()
        {
            // Arrange
            var requestDto = new AnalysisRequestDto
            {
                AnalysisType = AnalysisType.Selected,
                PromptKeys = new List<string> { "prompt1.txt", "prompt2.txt" },
                DocumentUrls = new List<string>()
            };

            var personaPrompt = "Master persona.";
            var subPrompt1 = "First sub-prompt.";
            var subPrompt2 = "Second sub-prompt.";
            var aggregatedSubPrompts = $"{subPrompt1}\n\n---\n\n{subPrompt2}";
            var expectedFinalPrompt = $"{personaPrompt}\n\n{aggregatedSubPrompts}";
            var expectedResult = "Multi-prompt analysis successful.";

            _mockPromptManager.Setup(p => p.GetPromptAsync(It.IsAny<string>(), SelectedAnalysisPersonaKey)).ReturnsAsync(personaPrompt);
            _mockPromptManager.Setup(p => p.GetPromptAsync(It.IsAny<string>(), "prompt1.txt")).ReturnsAsync(subPrompt1);
            _mockPromptManager.Setup(p => p.GetPromptAsync(It.IsAny<string>(), "prompt2.txt")).ReturnsAsync(subPrompt2);
            _mockAiService.Setup(a => a.PerformMultimodalAnalysisAsync(It.IsAny<IEnumerable<string>>(), expectedFinalPrompt)).ReturnsAsync(expectedResult);

            // Act
            var result = await _analysisService.PerformAnalysisAsync(requestDto);

            // Assert
            Assert.Equal(expectedResult, result);
            _mockPromptManager.Verify(p => p.GetPromptAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(3)); // 1 for persona, 2 for sub-prompts
            _mockAiService.Verify(a => a.PerformMultimodalAnalysisAsync(requestDto.DocumentUrls, expectedFinalPrompt), Times.Once);
        }

        [Fact]
        public async Task PerformAnalysisAsync_RenovationAnalysis_UsesSinglePromptAsFinal()
        {
            // Arrange
            var renovationPromptKey = "special_renovation_prompt.txt";
            var requestDto = new AnalysisRequestDto
            {
                AnalysisType = AnalysisType.Renovation,
                PromptKeys = new List<string> { renovationPromptKey },
                DocumentUrls = new List<string> { "http://example.com/plan.jpg" }
            };

            var renovationPromptContent = "This is the full renovation prompt content.";
            var expectedResult = "Renovation analysis complete.";

            // For Renovation, the key's content IS the final prompt. No persona is used.
            _mockPromptManager.Setup(p => p.GetPromptAsync(It.IsAny<string>(), renovationPromptKey)).ReturnsAsync(renovationPromptContent);
            _mockAiService.Setup(a => a.PerformMultimodalAnalysisAsync(requestDto.DocumentUrls, renovationPromptContent)).ReturnsAsync(expectedResult);

            // Act
            var result = await _analysisService.PerformAnalysisAsync(requestDto);

            // Assert
            Assert.Equal(expectedResult, result);
            // Verify that only the specific renovation prompt was fetched.
            _mockPromptManager.Verify(p => p.GetPromptAsync(It.IsAny<string>(), renovationPromptKey), Times.Once);
            // Verify no other prompts (like a persona) were fetched.
            _mockPromptManager.VerifyNoOtherCalls();
            _mockAiService.Verify(a => a.PerformMultimodalAnalysisAsync(requestDto.DocumentUrls, renovationPromptContent), Times.Once);
        }

        [Fact]
        public async Task PerformAnalysisAsync_AiFailureResponse_TriggersCorrectiveAction()
        {
            // Arrange
            var requestDto = new AnalysisRequestDto
            {
                AnalysisType = AnalysisType.Selected,
                PromptKeys = new List<string> { "prompt1.txt" },
                DocumentUrls = new List<string> { "doc1.pdf" }
            };

            var personaPrompt = "Persona";
            var subPrompt = "Sub-prompt";
            var initialFinalPrompt = $"{personaPrompt}\n\n{subPrompt}";
            var failedResponse = "I cannot fulfill this request.";

            var correctivePrompt = "This is the corrective action prompt.";
            var correctiveInput = $"{correctivePrompt}\n\nOriginal Failed Response:\n{failedResponse}";
            var finalCorrectedResult = "Corrective action successful.";

            _mockPromptManager.Setup(p => p.GetPromptAsync(It.IsAny<string>(), SelectedAnalysisPersonaKey)).ReturnsAsync(personaPrompt);
            _mockPromptManager.Setup(p => p.GetPromptAsync(It.IsAny<string>(), "prompt1.txt")).ReturnsAsync(subPrompt);
            _mockPromptManager.Setup(p => p.GetPromptAsync(It.IsAny<string>(), FailureCorrectiveActionKey)).ReturnsAsync(correctivePrompt);

            // Initial call fails
            _mockAiService.Setup(a => a.PerformMultimodalAnalysisAsync(requestDto.DocumentUrls, initialFinalPrompt)).ReturnsAsync(failedResponse);
            // Corrective call succeeds
            _mockAiService.Setup(a => a.PerformMultimodalAnalysisAsync(requestDto.DocumentUrls, correctiveInput)).ReturnsAsync(finalCorrectedResult);

            // Act
            var result = await _analysisService.PerformAnalysisAsync(requestDto);

            // Assert
            Assert.Equal(finalCorrectedResult, result);
            // Verify initial analysis was attempted
            _mockAiService.Verify(a => a.PerformMultimodalAnalysisAsync(requestDto.DocumentUrls, initialFinalPrompt), Times.Once);
            // Verify corrective prompt was fetched
            _mockPromptManager.Verify(p => p.GetPromptAsync(It.IsAny<string>(), FailureCorrectiveActionKey), Times.Once);
            // Verify corrective analysis was performed
            _mockAiService.Verify(a => a.PerformMultimodalAnalysisAsync(requestDto.DocumentUrls, correctiveInput), Times.Once);
        }

        [Fact]
        public async Task PerformAnalysisAsync_NoPromptKeys_ThrowsArgumentException()
        {
            // Arrange
            var requestDto = new AnalysisRequestDto
            {
                AnalysisType = AnalysisType.Selected,
                PromptKeys = new List<string>() // Empty list
            };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => _analysisService.PerformAnalysisAsync(requestDto));
            Assert.Equal("At least one prompt key must be provided. (Parameter 'PromptKeys')", exception.Message);
        }
    }
}
