using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Models.Enums;

namespace ProbuildBackend.Services
{
    public class AnalysisService : IAnalysisService
    {
        private readonly ILogger<AnalysisService> _logger;
        private readonly IPromptManagerService _promptManager;
        private readonly IAiService _aiService;

        // Constants for persona prompt keys
        private const string SelectedAnalysisPersonaKey = "sub-contractor-selected-prompt-master-prompt.txt";
        private const string RenovationAnalysisPersonaKey = "ProBuildAI_Renovation_Prompt.txt";
        private const string FailureCorrectiveActionKey = "prompt-failure-corrective-action.txt";


        public AnalysisService(ILogger<AnalysisService> logger, IPromptManagerService promptManager, IAiService aiService)
        {
            _logger = logger;
            _promptManager = promptManager;
            _aiService = aiService;
        }

        public async Task<string> PerformAnalysisAsync(AnalysisRequestDto requestDto)
        {
            if (requestDto?.PromptKeys == null || !requestDto.PromptKeys.Any())
            {
                throw new ArgumentException("At least one prompt key must be provided.", nameof(requestDto.PromptKeys));
            }

            try
            {
                string finalPrompt;

                if (requestDto.AnalysisType == AnalysisType.Renovation)
                {
                    // For Renovation, the single prompt key IS the full prompt.
                    string renovationPromptKey = requestDto.PromptKeys.Single();
                    _logger.LogInformation("Performing 'Renovation' analysis with single prompt: {PersonaKey}", renovationPromptKey);
                    finalPrompt = await _promptManager.GetPromptAsync(null, renovationPromptKey);
                }
                else // Handles AnalysisType.Selected
                {
                    // For Selected, we have a master persona and sub-prompts.
                    string personaPromptKey = GetPersonaKeyForAnalysisType(requestDto.AnalysisType);
                    _logger.LogInformation("Performing '{AnalysisType}' analysis with persona: {PersonaKey}", requestDto.AnalysisType, personaPromptKey);

                    string personaPrompt = await _promptManager.GetPromptAsync(null, personaPromptKey);

                    var subPromptsContent = await Task.WhenAll(
                        requestDto.PromptKeys.Select(key => _promptManager.GetPromptAsync(null, key))
                    );
                    string aggregatedSubPrompts = string.Join("\n\n---\n\n", subPromptsContent.Where(s => !string.IsNullOrEmpty(s)));

                    finalPrompt = $"{personaPrompt}\n\n{aggregatedSubPrompts}";
                }

                // Execute the analysis with the correctly constructed prompt.
                var analysisResult = await _aiService.PerformMultimodalAnalysisAsync(requestDto.DocumentUrls, finalPrompt);

                // Check for failure keywords
                if (analysisResult.Contains("cannot fulfill", StringComparison.OrdinalIgnoreCase) ||
                    analysisResult.Contains("unable to process", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Initial analysis failed for prompts: {PromptKeys}. Triggering corrective action.", string.Join(", ", requestDto.PromptKeys));
                    return await HandleFailureAsync(requestDto.DocumentUrls, analysisResult);
                }

                _logger.LogInformation("Analysis completed successfully for prompts: {PromptKeys}", string.Join(", ", requestDto.PromptKeys));
                return analysisResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during analysis for prompts: {PromptKeys}", string.Join(", ", requestDto.PromptKeys));
                throw;
            }
        }

        private string GetPersonaKeyForAnalysisType(AnalysisType analysisType)
        {
            switch (analysisType)
            {
                case AnalysisType.Selected:
                    return SelectedAnalysisPersonaKey;
                case AnalysisType.Renovation:
                    return RenovationAnalysisPersonaKey;
                default:
                    _logger.LogWarning("Unhandled AnalysisType: {AnalysisType}", analysisType);
                    throw new ArgumentOutOfRangeException(nameof(analysisType), $"AnalysisType '{analysisType}' is not supported.");
            }
        }

        private async Task<string> HandleFailureAsync(IEnumerable<string> documentUrls, string failedResponse)
        {
            var correctivePrompt = await _promptManager.GetPromptAsync(null, FailureCorrectiveActionKey);
            var correctiveInput = $"{correctivePrompt}\n\nOriginal Failed Response:\n{failedResponse}";

            // The corrective action requires the original documents and the failed response.
            return await _aiService.PerformMultimodalAnalysisAsync(documentUrls, correctiveInput);
        }
    }
}
