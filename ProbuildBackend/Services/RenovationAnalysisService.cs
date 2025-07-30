using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Services
{
    public class RenovationAnalysisService : IRenovationAnalysisService
    {
        private readonly IAiService _aiService;
        private readonly IPromptManagerService _promptManager;

        public RenovationAnalysisService(IAiService aiService, IPromptManagerService promptManager)
        {
            _aiService = aiService;
            _promptManager = promptManager;
        }

        public async Task<AnalysisResponse> PerformAnalysisAsync(RenovationAnalysisRequest request)
        {
            var prompt = await _promptManager.GetPromptAsync("RenovationPrompts/", "ProBuildAI_Renovation_Prompt.txt");

            // For now, we'll start a new conversation for each analysis.
            // In the future, we might want to manage conversations more dynamically.
            var (analysisResult, conversationId) = await _aiService.StartMultimodalConversationAsync(request.UserId, null, prompt, "Analyze the renovation project based on the provided details.");

            return new AnalysisResponse
            {
                AnalysisResult = analysisResult,
                ConversationId = conversationId
            };
        }
    }
}