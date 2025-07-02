// ProbuildBackend/Services/ProjectAnalysisOrchestrator.cs
using Microsoft.Extensions.Logging;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

public class ProjectAnalysisOrchestrator : IProjectAnalysisOrchestrator
{
    private readonly IAiService _aiService;
    private readonly IPromptManagerService _promptManager;
    private readonly ILogger<ProjectAnalysisOrchestrator> _logger;

    public ProjectAnalysisOrchestrator(IAiService aiService, IPromptManagerService promptManager, ILogger<ProjectAnalysisOrchestrator> logger)
    {
        _aiService = aiService;
        _promptManager = promptManager;
        _logger = logger;
    }

    public async Task<string> StartFullAnalysisAsync(string userId, IEnumerable<byte[]> blueprintImages, JobModel jobDetails)
    {
        _logger.LogInformation("Starting full analysis for user {UserId}", userId);
        string? conversationId = null;
        
        var initialTaskPrompt = await _promptManager.GetPromptAsync("prompt-00-initial-analysis");
        
        // Build a detailed setup message using the full JobModel
        var userSetupMessage = $@"
Please begin the initial analysis on the attached blueprint images for the following project.
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

Now, please execute the initial analysis task based on this information and the provided blueprints.

{initialTaskPrompt}";

        var (_, newConversationId) = await _aiService.ContinueConversationAsync(conversationId, userId, userSetupMessage, blueprintImages);
        conversationId = newConversationId;
        _logger.LogInformation("Started conversation {ConversationId}", conversationId);

        var promptNames = new[] {
            "prompt-01-sitelogistics", "prompt-02-groundwork", "prompt-03-framing",
            "prompt-04-roofing", "prompt-05-exterior", "prompt-06-electrical",
            "prompt-07-plumbing", "prompt-08-hvac", "prompt-09-insulation",
            "prompt-10-drywall", "prompt-11-painting", "prompt-12-trim",
            "prompt-13-kitchenbath", "prompt-14-flooring", "prompt-15-exteriorflatwork",
            "prompt-16-cleaning", "prompt-17-costbreakdowns", "prompt-18-riskanalyst",
            "prompt-19-timeline", "prompt-20-environmental", "prompt-21-closeout"
        };

        int step = 1;
        foreach (var promptName in promptNames)
        {
            _logger.LogInformation("Executing step {Step}: {PromptName}", step, promptName);
            var promptText = await _promptManager.GetPromptAsync(promptName);
            await _aiService.ContinueConversationAsync(conversationId, userId, promptText, null);
            step++;
        }
        _logger.LogInformation("Full analysis completed for conversation {ConversationId}", conversationId);
        return conversationId;
    }

    public async Task<string> GenerateRebuttalAsync(string conversationId, string clientQuery)
    {
        var rebuttalPrompt = await _promptManager.GetPromptAsync("prompt-22-rebuttal") + $"\n\n**Client Query to Address:**\n{clientQuery}";
        var (response, _) = await _aiService.ContinueConversationAsync(conversationId, "system-user", rebuttalPrompt, null);
        return response;
    }

    public async Task<string> GenerateRevisionAsync(string conversationId, string revisionRequest)
    {
        var revisionPrompt = await _promptManager.GetPromptAsync("prompt-revision") + $"\n\n**Revision Request:**\n{revisionRequest}";
        var (response, _) = await _aiService.ContinueConversationAsync(conversationId, "system-user", revisionPrompt, null);
        return response;
    }
}