using ProbuildBackend.Interface;
using ProbuildBackend.Models;

public class ProjectAnalysisOrchestrator : IProjectAnalysisOrchestrator
{
    private readonly IAiService _aiService;
    private readonly IPromptManagerService _promptManager;
    private readonly ILogger<ProjectAnalysisOrchestrator> _logger;
    private readonly IComprehensiveAnalysisService _comprehensiveAnalysisService;

    public ProjectAnalysisOrchestrator(IAiService aiService, IPromptManagerService promptManager, ILogger<ProjectAnalysisOrchestrator> logger, IComprehensiveAnalysisService comprehensiveAnalysisService)
    {
        _aiService = aiService;
        _promptManager = promptManager;
        _logger = logger;
        _comprehensiveAnalysisService = comprehensiveAnalysisService;
    }

    public async Task<string> StartFullAnalysisAsync(string userId, IEnumerable<byte[]> blueprintImages, JobModel jobDetails)
    {
        _logger.LogInformation("Orchestrating full analysis for user {UserId}", userId);
        return await _comprehensiveAnalysisService.PerformAnalysisFromImagesAsync(userId, blueprintImages, jobDetails);
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