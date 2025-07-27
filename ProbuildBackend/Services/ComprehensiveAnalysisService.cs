using ProbuildBackend.Interface;
using ProbuildBackend.Models;

namespace ProbuildBackend.Services
{
    public class ComprehensiveAnalysisService : IComprehensiveAnalysisService
    {
        private readonly IAiService _aiService;
        private readonly IPromptManagerService _promptManager;
        private readonly ILogger<ComprehensiveAnalysisService> _logger;

        public ComprehensiveAnalysisService(IAiService aiService, IPromptManagerService promptManager, ILogger<ComprehensiveAnalysisService> logger)
        {
            _aiService = aiService;
            _promptManager = promptManager;
            _logger = logger;
        }

        [Obsolete("This method is deprecated and will be removed in a future version. Use PerformAnalysisFromFilesAsync instead.")]
        public async Task<string> PerformAnalysisFromTextAsync(string userId, string fullText, JobModel jobDetails)
        {
            _logger.LogInformation("Starting comprehensive analysis from text for user {UserId}", userId);
            string? conversationId = null;

            // Step 1: Initial Health Check
            var initialTaskPrompt = await _promptManager.GetPromptAsync("prompt-00-initial-analysis");
            var (initialResponseText, newConversationId) = await _aiService.ContinueConversationAsync(conversationId, userId, initialTaskPrompt + "\n\n" + fullText, null);
            conversationId = newConversationId;
            _logger.LogInformation("Initial fitness check completed for conversation {ConversationId}.", conversationId);

            // Step 2: Check for "BLUEPRINT FAILURE"
            if (initialResponseText.Trim().Contains("BLUEPRINT FAILURE", StringComparison.CurrentCultureIgnoreCase))
            {
                _logger.LogWarning("Blueprint FAILURE detected for conversation {ConversationId}. Halting analysis and generating corrective action report.", conversationId);
                var failurePromptText = await _promptManager.GetPromptAsync("prompt-failure-corrective-action");
                var (failureReport, _) = await _aiService.ContinueConversationAsync(conversationId, userId, failurePromptText, null);
                return failureReport;
            }

            _logger.LogInformation("Blueprint fitness check PASSED for conversation {ConversationId}. Proceeding with full sequential analysis.", conversationId);

            // Step 3: Construct detailed setup message
            var userSetupMessage = $@"
Please perform a comprehensive analysis based on the following project details and the provided document text.
Use these details as the primary source of information to guide your analysis:

Project Name: {jobDetails.ProjectName}
Job Type: {jobDetails.JobType}
Address: {jobDetails.Address}
Operating Area / Location for Localization: {jobDetails.OperatingArea}
Desired Start Date: {jobDetails.DesiredStartDate:yyyy-MM-dd}
Stories: {jobDetails.Stories}
Building Size: {jobDetails.BuildingSize} sq ft
Client-Specified Assumptions:
Wall Structure: {jobDetails.WallStructure}
Wall Insulation: {jobDetails.WallInsulation}
Roof Structure: {jobDetails.RoofStructure}
Roof Insulation: {jobDetails.RoofInsulation}
Foundation: {jobDetails.Foundation}
Finishes: {jobDetails.Finishes}
Electrical Needs: {jobDetails.ElectricalSupplyNeeds}

Provided Document Text:
---
{fullText}
---

Now, please execute the full analysis based on this information. I will provide you with a sequence of prompts to follow.
";

            // Step 4: Start the main conversation flow
            await _aiService.ContinueConversationAsync(conversationId, userId, userSetupMessage, null);
            _logger.LogInformation("Started main analysis for conversation {ConversationId}.", conversationId);

            return await ExecuteSequentialPromptsAsync(conversationId, userId, initialResponseText);
        }

        public async Task<string> PerformAnalysisFromImagesAsync(string userId, IEnumerable<byte[]> blueprintImages, JobModel jobDetails)
        {
            _logger.LogInformation("Starting comprehensive analysis from images for user {UserId}", userId);
            string? conversationId = null;

            // TODO: Implement the initial AI conversation setup with images.
            // For now, log a message.
            _logger.LogInformation("Image analysis path not fully implemented. This is a placeholder.");

            // This is a placeholder for where the conversation would be initiated with images.
            // var (initialResponse, newConversationId) = await _aiService.StartConversationWithImagesAsync(....);
            // conversationId = newConversationId;

            // For now, can't proceed without a conversation ID.
            
            return await Task.FromResult("Image analysis feature is under development.");
        }

        public async Task<string> PerformAnalysisFromFilesAsync(string userId, IEnumerable<string> documentUris, JobModel jobDetails)
        {
            _logger.LogInformation("Starting stateful analysis from files for user {UserId}", userId);

            // 1. Get Prompts
            var systemPersonaPrompt = await _promptManager.GetPromptAsync("system-persona");
            var initialAnalysisPrompt = await _promptManager.GetPromptAsync("prompt-00-initial-analysis");

            // Construct the full initial prompt with job details
            var initialUserPrompt = $"{initialAnalysisPrompt}\n\nHere are the project details:\n" +
                                    $"Project Name: {jobDetails.ProjectName}\n" +
                                    $"Job Type: {jobDetails.JobType}\n" +
                                    $"Address: {jobDetails.Address}\n" +
                                    $"Operating Area: {jobDetails.OperatingArea}\n" +
                                    $"Desired Start Date: {jobDetails.DesiredStartDate:yyyy-MM-dd}\n" +
                                    $"Stories: {jobDetails.Stories}\n" +
                                    $"Building Size: {jobDetails.BuildingSize} sq ft\n" +
                                    $"Wall Structure: {jobDetails.WallStructure}\n" +
                                    $"Wall Insulation: {jobDetails.WallInsulation}\n" +
                                    $"Roof Structure: {jobDetails.RoofStructure}\n" +
                                    $"Roof Insulation: {jobDetails.RoofInsulation}\n" +
                                    $"Foundation: {jobDetails.Foundation}\n" +
                                    $"Finishes: {jobDetails.Finishes}\n" +
                                    $"Electrical Needs: {jobDetails.ElectricalSupplyNeeds}";

            try
            {
                // 2. Start Conversation
                var (initialResponse, conversationId) = await _aiService.StartMultimodalConversationAsync(userId, documentUris, systemPersonaPrompt, initialUserPrompt);
                _logger.LogInformation("Started multimodal conversation {ConversationId} for user {UserId}", conversationId, userId);

                // 3. Health Check
                if (initialResponse.Contains("BLUEPRINT FAILURE", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Blueprint FAILURE detected for conversation {ConversationId}. Halting analysis.", conversationId);
                    // Optionally, could call ContinueConversationAsync here to ask the AI to elaborate on the failure.
                    return initialResponse; // Return the failure report
                }

                _logger.LogInformation("Blueprint fitness check PASSED for conversation {ConversationId}. Proceeding with full sequential analysis.", conversationId);

                // 4. Execute Sequential Prompts
                return await ExecuteSequentialPromptsAsync(conversationId, userId, initialResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during PerformAnalysisFromFilesAsync for user {UserId}", userId);
                throw;
            }
        }

        private async Task<string> ExecuteSequentialPromptsAsync(string conversationId, string userId, string initialResponse)
        {
            var stringBuilder = new System.Text.StringBuilder();
            stringBuilder.Append(initialResponse);

            var promptNames = new[] {
                "prompt-01-sitelogistics", "prompt-02-groundwork", "prompt-03-framing",
                "prompt-04-roofing", "prompt-05-exterior", "prompt-06-electrical",
                "prompt-07-plumbing", "prompt-08-hvac", "prompt-09-insulation",
                "prompt-10-drywall", "prompt-11-painting", "prompt-12-trim",
                "prompt-13-kitchenbath", "prompt-14-flooring", "prompt-15-exteriorflatwork",
                "prompt-16-cleaning", "prompt-17-costbreakdowns", "prompt-18-riskanalyst",
                "prompt-19-timeline", "prompt-20-environmental", "prompt-21-closeout"
            };

            string lastResponse;
            int step = 1;
            foreach (var promptName in promptNames)
            {
                _logger.LogInformation("Executing step {Step} of {TotalSteps}: {PromptName} for conversation {ConversationId}", step, promptNames.Length, promptName, conversationId);
                var promptText = await _promptManager.GetPromptAsync(promptName);
                (lastResponse, _) = await _aiService.ContinueConversationAsync(conversationId, userId, promptText, null);

                stringBuilder.Append("\n\n---\n\n");
                stringBuilder.Append(lastResponse);

                step++;
            }

            _logger.LogInformation("Full sequential analysis completed successfully for conversation {ConversationId}", conversationId);

            return stringBuilder.ToString();
        }
    }
}