using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BuildigBackend.Interface;
using BuildigBackend.Models;
using BuildigBackend.Models.DTO;
using BuildigBackend.Models.Enums;

namespace BuildigBackend.Services
{
    public class AiAnalysisService : IAiAnalysisService
    {
        private readonly ILogger<AiAnalysisService> _logger;
        private readonly IPromptManagerService _promptManager;
        private readonly IAiService _aiService;
        private readonly IConversationRepository _conversationRepo;
        private readonly ApplicationDbContext _context;
        private readonly IPdfTextExtractionService _pdfTextExtractionService;
        private readonly AzureBlobService _azureBlobService;
        private readonly IHubContext<Middleware.ProgressHub> _hubContext;
        private readonly IKeepAliveService _keepAliveService;

        private const string SelectedAnalysisPersonaKey = "selected-prompt-system-persona.txt";
        private const string RenovationAnalysisPersonaKey = "Build IG_Renovation_Prompt.txt";
        private const string FailureCorrectiveActionKey = "prompt-failure-corrective-action.txt";
        private const string BidComparisonPromptKey = "subcontractor-comparison-prompt.txt";
        private const string InitialAnalysisPhaseTitle = "Initial Analysis & Reporting";
        private const double ScopeCostVarianceRejectThreshold = 0.15;
        private const decimal DirectTotalsMismatchRejectAbsoluteTolerance = 1.00m;
        private const decimal DirectTotalsMismatchRejectRelativeTolerance = 0.01m;
        private const string RejectionReasonPrefix = "__SCOPE_REJECTION_REASON__:";
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _analysisLocks = new();
        private static int _jobPromptResultsTableDiagnosticsLogged = 0;
        private static int _jobPromptResultsTableEnsured = 0;
        private static readonly string _instanceId = Guid.NewGuid().ToString("N");

        public AiAnalysisService(
            ILogger<AiAnalysisService> logger,
            IPromptManagerService promptManager,
            IAiService aiService,
            IConversationRepository conversationRepo,
            ApplicationDbContext context,
            IPdfTextExtractionService pdfTextExtractionService,
            AzureBlobService azureBlobService,
            IHubContext<Middleware.ProgressHub> hubContext,
            IKeepAliveService keepAliveService
        )
        {
            _logger = logger;
            _promptManager = promptManager;
            _aiService = aiService;
            _conversationRepo = conversationRepo;
            _context = context;
            _pdfTextExtractionService = pdfTextExtractionService;
            _azureBlobService = azureBlobService;
            _hubContext = hubContext;
            _keepAliveService = keepAliveService;
        }

        public async Task<string> PerformSelectedAnalysisAsync(
            string userId,
            AnalysisRequestDto requestDto,
            bool generateDetailsWithAi,
            string budgetLevel,
            string? conversationId = null,
            string? connectionId = null
        )
        {
            _keepAliveService.StartPinging();
            var analysisLock = _analysisLocks.GetOrAdd(requestDto.JobId, _ => new SemaphoreSlim(1, 1));
            await analysisLock.WaitAsync();
            var analysisStartedAtUtc = DateTime.UtcNow;
            try
            {
                if (requestDto?.PromptKeys == null || !requestDto.PromptKeys.Any())
                {
                    throw new ArgumentException(
                        "At least one prompt key must be provided.",
                        nameof(requestDto.PromptKeys)
                    );
                }

                var job = await _context.Jobs.FindAsync(requestDto.JobId);
                var title =
                    $"Selected Analysis for {job?.ProjectName ?? "Job ID " + requestDto.JobId}";

                if (string.IsNullOrEmpty(conversationId))
                {
                    if (!string.IsNullOrEmpty(requestDto.ConversationId))
                    {
                        conversationId = requestDto.ConversationId;
                    }
                    else if (!string.IsNullOrEmpty(job.ConversationId))
                    {
                        conversationId = job.ConversationId;
                    }
                }

                string personaPromptKey = SelectedAnalysisPersonaKey;
                _logger.LogInformation(
                    "Performing 'Selected' analysis with persona: {PersonaKey}",
                    personaPromptKey
                );

                string personaPrompt = await _promptManager.GetPromptAsync(null, personaPromptKey);
                var userContext = await GetUserContextAsString(requestDto.UserContext, null);
                var budgetPrompt = await _promptManager.GetPromptAsync(
                    null,
                    $"{budgetLevel}-budget-prompt.txt"
                );

                var (initialResponse, newConversationId) =
                    await _aiService.StartMultimodalConversationAsync(
                        userId,
                        requestDto.DocumentUrls,
                        personaPrompt,
                        budgetPrompt + "\n" + userContext,
                        conversationId
                    );

                // Link Conversation to Job if not already linked
                if (job.ConversationId != newConversationId)
                {
                    job.ConversationId = newConversationId;
                    await _context.SaveChangesAsync();
                }
                conversationId = newConversationId;

                if (
                    initialResponse.Contains(
                        "BLUEPRINT FAILURE",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    _logger.LogWarning(
                        "Initial analysis failed for prompts: {PromptKeys}. Triggering corrective action.",
                        string.Join(", ", requestDto.PromptKeys)
                    );
                    initialResponse = await HandleFailureAsync(
                        conversationId,
                        userId,
                        requestDto.DocumentUrls,
                        initialResponse
                    );
                }

                initialResponse = EnsurePhaseHeading(initialResponse, 1, InitialAnalysisPhaseTitle);

                var reportBuilder = new StringBuilder();
                reportBuilder.Append(initialResponse);

                // Remove the JSON requirement from the persona for subsequent calls
                var personaWithoutJson = new Regex(
                    @"CRITICAL OUTPUT REQUIREMENT:.*?\}",
                    RegexOptions.Singleline
                ).Replace(personaPrompt, ""); // TODO: Check this, is it causing issues with the subsequent prompts?

                var dailyPlanPromptKey = "selected-prompt-daily-plan.txt";
                var orderedPrompts = requestDto
                    .PromptKeys.Where(p => p != dailyPlanPromptKey)
                    .ToList();
                orderedPrompts.Add(dailyPlanPromptKey);

                var completedPrompts = await GetCompletedPromptNames(job.Id);
                var savedModelPrompts = await GetSavedModelPromptNames(job.Id);
                foreach (var promptName in savedModelPrompts)
                {
                    if (!completedPrompts.Contains(promptName))
                    {
                        // Do not mark completed just because the model response was saved.
                        // A prompt is only considered completed once we have persisted a JobPromptResult.
                        var normalizedPromptKey = NormalizePromptKey(promptName);
                        var hasPersisted = await _context.JobPromptResults.AsNoTracking()
                            .AnyAsync(r => r.JobId == job.Id && r.PromptKey == normalizedPromptKey);
                        if (hasPersisted)
                        {
                            await MarkPromptCompleted(job.Id, normalizedPromptKey);
                            completedPrompts.Add(normalizedPromptKey);
                        }
                    }
                }

                // Resume safety: if a prompt is marked completed but no JobPromptResult exists,
                // treat it as incomplete so the prompt reruns and persists.
                foreach (var promptKey in orderedPrompts)
                {
                    if (!completedPrompts.Contains(promptKey) || IsScopeSnapshotPromptKey(promptKey))
                    {
                        continue;
                    }

                    var normalizedPromptKey = NormalizePromptKey(promptKey);
                    var hasPersisted = await _context.JobPromptResults.AsNoTracking()
                        .AnyAsync(r => r.JobId == job.Id && r.PromptKey == normalizedPromptKey);
                    if (!hasPersisted)
                    {
                        _logger.LogWarning(
                            "Resume detected completed prompt without JobPromptResults row. Will rerun: jobId={JobId} promptKey={PromptKey}",
                            job.Id,
                            normalizedPromptKey
                        );
                        completedPrompts.Remove(promptKey);
                        completedPrompts.Remove(normalizedPromptKey);
                    }
                }

                int step = completedPrompts.Count + 1;
                foreach (var promptKey in orderedPrompts)
                {
                    if (
                        completedPrompts.Contains(promptKey)
                        && !IsScopeSnapshotPromptKey(promptKey)
                    )
                    {
                        continue;
                    }

                    await TrySendAnalysisProgressUpdate(
                        connectionId,
                        new Middleware.AnalysisProgressUpdate
                        {
                            JobId = job.Id,
                            StatusMessage = $"Analyzing: {FormatPromptStatusLabel(promptKey)}",
                            CurrentStep = step,
                            TotalSteps = orderedPrompts.Count,
                            IsComplete = false,
                            HasFailed = false,
                        }
                    );

                    await UpdateAnalysisState(
                        job.Id,
                        $"Analyzing: {FormatPromptStatusLabel(promptKey)}",
                        step,
                        orderedPrompts.Count
                    );

                    var subPrompt = await _promptManager.GetPromptAsync(null, promptKey);
                    subPrompt = await ApplyDeterministicScopePromptInstructionsAsync(
                        job.Id,
                        promptKey,
                        subPrompt
                    );
                    var phaseTitle = FormatPromptStatusLabel(promptKey);
                    var phasePrefix = BuildPhaseInstructionPrefix(step, phaseTitle);
                    var (analysisResult, _) = await _aiService.ContinueConversationAsync(
                        conversationId,
                        userId,
                        $"{phasePrefix}\n\n{subPrompt}",
                        requestDto.DocumentUrls,
                        true,
                        personaWithoutJson
                    );
                    analysisResult = EnsurePhaseHeading(analysisResult, step, phaseTitle);
                    var message = new Message
                    {
                        ConversationId = conversationId,
                        Role = "model",
                        Content = analysisResult,
                        Timestamp = DateTime.UtcNow,
                    };
                    await _conversationRepo.AddMessageIfNotExistsAsync(message);

                    await MarkModelPromptSaved(job.Id, promptKey);
                    reportBuilder.Append("\n\n---\n\n");
                    reportBuilder.Append(analysisResult);

                    var persisted = await ParseAndBroadcastPromptResult(
                        job.Id,
                        promptKey,
                        analysisResult,
                        connectionId
                    );

                    if (persisted)
                    {
                        await MarkPromptCompleted(job.Id, promptKey);
                        completedPrompts.Add(promptKey);
                    }

                    step++;
                }

                // Extract and execute Timeline and Cost prompts
                var timelinePromptRegex = new Regex(
                    @"2\. Timeline Prompt:.*?(?=3\. Cost Prompt:)",
                    RegexOptions.Singleline
                );
                var timelineMatch = timelinePromptRegex.Match(personaPrompt);
                if (timelineMatch.Success)
                {
                    var timelinePrompt = timelineMatch.Value;
                    var (timelineResult, _) = await _aiService.ContinueConversationAsync(
                        conversationId,
                        userId,
                        timelinePrompt,
                        requestDto.DocumentUrls,
                        true,
                        personaWithoutJson
                    );
                    reportBuilder.Append("\n\n---\n\n");
                    reportBuilder.Append(timelineResult);
                    await ParseAndBroadcastPromptResult(
                        job.Id,
                        "prompt-21-timeline",
                        timelineResult,
                        connectionId
                    );
                }

                var costPromptRegex = new Regex(@"3\. Cost Prompt:.*", RegexOptions.Singleline);
                var costMatch = costPromptRegex.Match(personaPrompt);
                if (costMatch.Success)
                {
                    var costPrompt = costMatch.Value;
                    var (costResult, _) = await _aiService.ContinueConversationAsync(
                        conversationId,
                        userId,
                        costPrompt,
                        requestDto.DocumentUrls,
                        true,
                        personaWithoutJson
                    );
                    reportBuilder.Append("\n\n---\n\n");
                    reportBuilder.Append(costResult);
                    await ParseAndBroadcastPromptResult(
                        job.Id,
                        "prompt-25-cost-breakdowns",
                        costResult,
                        connectionId
                    );
                }

                if (job != null && generateDetailsWithAi)
                {
                    await ParseAndSaveAiJobDetails(job.Id, reportBuilder.ToString());
                }

                await EnsureScopeSnapshotCompletenessAsync(job.Id, analysisStartedAtUtc);

                var completedJob = await _context.Jobs.FindAsync(job.Id);
                if (completedJob != null)
                {
                    completedJob.Status = "PRELIMINARY";
                    await _context.SaveChangesAsync();
                }

                await TrySendAnalysisProgressUpdate(
                    connectionId,
                    new Middleware.AnalysisProgressUpdate
                    {
                        JobId = job.Id,
                        StatusMessage = "Analysis complete.",
                        CurrentStep = orderedPrompts.Count,
                        TotalSteps = orderedPrompts.Count,
                        IsComplete = true,
                        HasFailed = false,
                    }
                );

                _logger.LogInformation(
                    "Analysis completed successfully for prompts: {PromptKeys}",
                    string.Join(", ", requestDto.PromptKeys)
                );
                return reportBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An error occurred during analysis for prompts: {PromptKeys}",
                    string.Join(", ", requestDto.PromptKeys)
                );
                var jobForError = await _context.Jobs.FindAsync(requestDto.JobId);
                if (jobForError != null)
                {
                    await TrySendAnalysisProgressUpdate(
                        connectionId,
                        new Middleware.AnalysisProgressUpdate
                        {
                            JobId = jobForError.Id,
                            StatusMessage = "Analysis failed.",
                            IsComplete = false,
                            HasFailed = true,
                            ErrorMessage = ex.Message,
                        }
                    );
                }
                throw;
            }
            finally
            {
                analysisLock.Release();
                _keepAliveService.StopPinging();
            }
        }

        public async Task<string> PerformComprehensiveAnalysisAsync(
            string userId,
            IEnumerable<string> documentUris,
            JobModel jobDetails,
            bool generateDetailsWithAi,
            string userContextText,
            string userContextFileUrl,
            string budgetLevel,
            string? connectionId = null,
            string promptKey = "prompt-00-initial-analysis.txt"
        )
        {
            _logger.LogInformation(
                "START: PerformComprehensiveAnalysisAsync for User {UserId}, Job {JobId}",
                userId,
                jobDetails.Id
            );
            _keepAliveService.StartPinging();
            var analysisLock = _analysisLocks.GetOrAdd(jobDetails.Id, _ => new SemaphoreSlim(1, 1));
            await analysisLock.WaitAsync();
            var analysisStartedAtUtc = DateTime.UtcNow;
            try
            {
                _logger.LogInformation("Fetching system persona prompt.");
                var systemPersonaPrompt = await _promptManager.GetPromptAsync(
                    "",
                    "system-persona.txt"
                );
                _logger.LogInformation("Fetching initial analysis prompt: {PromptKey}", promptKey);
                var initialAnalysisPrompt = await _promptManager.GetPromptAsync("", promptKey);

                _logger.LogInformation("Getting user context as string.");
                var userContext = await GetUserContextAsString(userContextText, userContextFileUrl);
                var budgetPrompt = await _promptManager.GetPromptAsync(
                    null,
                    $"{budgetLevel}-budget-prompt.txt"
                );

                var initialUserPrompt =
                    $"{budgetPrompt}\n\n{initialAnalysisPrompt}\n\n{userContext}\n\nHere are the project details:\n"
                    + $"Project Name: {jobDetails.ProjectName}\n"
                    + $"Job Type: {jobDetails.JobType}\n"
                    + $"Address: {jobDetails.Address}\n"
                    + $"Operating Area: {jobDetails.OperatingArea}\n"
                    + $"Desired Start Date: {jobDetails.DesiredStartDate:yyyy-MM-dd}\n"
                    + $"Stories: {jobDetails.Stories}\n"
                    + $"Building Size: {jobDetails.BuildingSize} sq ft\n"
                    + $"Wall Structure: {jobDetails.WallStructure}\n"
                    + $"Wall Insulation: {jobDetails.WallInsulation}\n"
                    + $"Roof Structure: {jobDetails.RoofStructure}\n"
                    + $"Roof Insulation: {jobDetails.RoofInsulation}\n"
                    + $"Foundation: {jobDetails.Foundation}\n"
                    + $"Finishes: {jobDetails.Finishes}\n"
                    + $"Electrical Needs: {jobDetails.ElectricalSupplyNeeds}";

                _logger.LogInformation(
                    "Initial user prompt created. Length: {Length}",
                    initialUserPrompt.Length
                );

                _logger.LogInformation("Calling StartMultimodalConversationAsync.");
                var existingConversationId = jobDetails.ConversationId;
                var completedPrompts = await GetCompletedPromptNames(jobDetails.Id);

                if (!string.IsNullOrWhiteSpace(existingConversationId) && completedPrompts.Count > 0)
                {
                    _logger.LogInformation(
                        "Resuming analysis for Job {JobId} using existing conversation {ConversationId}.",
                        jobDetails.Id,
                        existingConversationId
                    );

                    return await ExecuteSequentialPromptsAsync(
                        existingConversationId,
                        userId,
                        string.Empty,
                        jobDetails.Id,
                        connectionId,
                        analysisStartedAtUtc
                    );
                }

                var (initialResponse, conversationId) = await _aiService.StartMultimodalConversationAsync(
                    userId,
                    documentUris,
                    systemPersonaPrompt,
                    initialUserPrompt,
                    existingConversationId
                );
                _logger.LogInformation(
                    "Started multimodal conversation {ConversationId} for user {UserId}. Initial response length: {Length}",
                    conversationId,
                    userId,
                    initialResponse?.Length ?? 0
                );

                // Link Conversation to Job if not already linked
                if (jobDetails.ConversationId != conversationId)
                {
                    var trackedJob = await _context.Jobs.FindAsync(jobDetails.Id);
                    if (trackedJob != null)
                    {
                        trackedJob.ConversationId = conversationId;
                        await _context.SaveChangesAsync();
                    }
                }

                if (
                    initialResponse.Contains(
                        "BLUEPRINT FAILURE",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    _logger.LogWarning(
                        "Initial analysis failed for conversation {ConversationId}. Triggering corrective action.",
                        conversationId
                    );
                    return await HandleFailureAsync(
                        conversationId,
                        userId,
                        documentUris,
                        initialResponse
                    );
                }

                initialResponse = EnsurePhaseHeading(initialResponse, 1, InitialAnalysisPhaseTitle);

                _logger.LogInformation(
                    "Blueprint fitness check PASSED for conversation {ConversationId}. Proceeding with full sequential analysis.",
                    conversationId
                );

                if (generateDetailsWithAi)
                {
                    _logger.LogInformation(
                        "Parsing and saving AI job details for Job {JobId}",
                        jobDetails.Id
                    );
                    await ParseAndSaveAiJobDetails(jobDetails.Id, initialResponse);
                }

                _logger.LogInformation(
                    "Executing sequential prompts for conversation {ConversationId}",
                    conversationId
                );
                return await ExecuteSequentialPromptsAsync(
                    conversationId,
                    userId,
                    initialResponse,
                    jobDetails.Id,
                    connectionId,
                    analysisStartedAtUtc
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "EXCEPTION in PerformComprehensiveAnalysisAsync for User {UserId}, Job {JobId}",
                    userId,
                    jobDetails.Id
                );
                await TrySendAnalysisProgressUpdate(
                    connectionId,
                    new Middleware.AnalysisProgressUpdate
                    {
                        JobId = jobDetails.Id,
                        StatusMessage = "Analysis failed - we're sorry for the inconvenience",
                        IsComplete = false,
                        HasFailed = true,
                        ErrorMessage = ex.Message,
                    }
                );
                throw;
            }
            finally
            {
                analysisLock.Release();
                _logger.LogInformation(
                    "END: PerformComprehensiveAnalysisAsync for User {UserId}, Job {JobId}",
                    userId,
                    jobDetails.Id
                );
                _keepAliveService.StopPinging();
            }
        }

        public async Task<string> PerformRenovationAnalysisAsync(
            string userId,
            IEnumerable<string> documentUris,
            JobModel jobDetails,
            bool generateDetailsWithAi,
            string userContextText,
            string userContextFileUrl,
            string budgetLevel,
            string? connectionId = null,
            string promptKey = "renovation-00-initial-analysis.txt"
        )
        {
            _logger.LogInformation(
                "START: PerformRenovationAnalysisAsync for User {UserId}, Job {JobId}",
                userId,
                jobDetails.Id
            );
            _keepAliveService.StartPinging();
            var analysisLock = _analysisLocks.GetOrAdd(jobDetails.Id, _ => new SemaphoreSlim(1, 1));
            await analysisLock.WaitAsync();
            var analysisStartedAtUtc = DateTime.UtcNow;
            try
            {
                _logger.LogInformation("Fetching renovation persona prompt.");
                var personaPrompt = await _promptManager.GetPromptAsync(
                    "RenovationPrompts/",
                    "renovation-persona.txt"
                );
                _logger.LogInformation("Fetching initial renovation analysis prompt.");
                var initialAnalysisPrompt = await _promptManager.GetPromptAsync(
                    "RenovationPrompts/",
                    promptKey
                );

                _logger.LogInformation("Getting user context as string.");
                var userContext = await GetUserContextAsString(userContextText, userContextFileUrl);
                var budgetPrompt = await _promptManager.GetPromptAsync(
                    null,
                    $"{budgetLevel}-budget-prompt.txt"
                );

                var initialUserPrompt =
                    $"{budgetPrompt}\n\n{initialAnalysisPrompt}\n\n{userContext}";

                _logger.LogInformation(
                    "Calling StartMultimodalConversationAsync for renovation analysis."
                );

                var existingConversationId = jobDetails.ConversationId;
                var completedPrompts = await GetCompletedPromptNames(jobDetails.Id);
                if (!string.IsNullOrWhiteSpace(existingConversationId) && completedPrompts.Count > 0)
                {
                    _logger.LogInformation(
                        "Resuming renovation analysis for Job {JobId} using existing conversation {ConversationId}.",
                        jobDetails.Id,
                        existingConversationId
                    );

                    return await ExecuteSequentialRenovationPromptsAsync(
                        existingConversationId,
                        userId,
                        string.Empty,
                        jobDetails.Id,
                        connectionId,
                        analysisStartedAtUtc
                    );
                }

                var (initialResponse, conversationId) =
                    await _aiService.StartMultimodalConversationAsync(
                        userId,
                        documentUris,
                        personaPrompt,
                        initialUserPrompt,
                        existingConversationId
                    );
                _logger.LogInformation(
                    "Started multimodal conversation {ConversationId} for user {UserId}. Initial response length: {Length}",
                    conversationId,
                    userId,
                    initialResponse?.Length ?? 0
                );

                // Link Conversation to Job if not already linked
                if (jobDetails.ConversationId != conversationId)
                {
                    var trackedJob = await _context.Jobs.FindAsync(jobDetails.Id);
                    if (trackedJob != null)
                    {
                        trackedJob.ConversationId = conversationId;
                        await _context.SaveChangesAsync();
                    }
                }

                if (
                    initialResponse.Contains(
                        "BLUEPRINT FAILURE",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    _logger.LogWarning(
                        "Initial renovation analysis failed for conversation {ConversationId}. Triggering corrective action.",
                        conversationId
                    );
                    return await HandleFailureAsync(
                        conversationId,
                        userId,
                        documentUris,
                        initialResponse
                    );
                }

                initialResponse = EnsurePhaseHeading(initialResponse, 1, InitialAnalysisPhaseTitle);

                _logger.LogInformation(
                    "Blueprint fitness check PASSED for renovation conversation {ConversationId}. Proceeding with full sequential analysis.",
                    conversationId
                );

                if (generateDetailsWithAi)
                {
                    _logger.LogInformation(
                        "Parsing and saving AI job details for Job {JobId}",
                        jobDetails.Id
                    );
                    await ParseAndSaveAiJobDetails(jobDetails.Id, initialResponse);
                }

                _logger.LogInformation(
                    "Executing sequential renovation prompts for conversation {ConversationId}",
                    conversationId
                );
                return await ExecuteSequentialRenovationPromptsAsync(
                    conversationId,
                    userId,
                    initialResponse,
                    jobDetails.Id,
                    connectionId,
                    analysisStartedAtUtc
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "EXCEPTION in PerformRenovationAnalysisAsync for User {UserId}, Job {JobId}",
                    userId,
                    jobDetails.Id
                );
                await TrySendAnalysisProgressUpdate(
                    connectionId,
                    new Middleware.AnalysisProgressUpdate
                    {
                        JobId = jobDetails.Id,
                        StatusMessage = "Analysis failed.",
                        IsComplete = false,
                        HasFailed = true,
                        ErrorMessage = ex.Message,
                    }
                );
                throw;
            }
            finally
            {
                analysisLock.Release();
                _logger.LogInformation(
                    "END: PerformRenovationAnalysisAsync for User {UserId}, Job {JobId}",
                    userId,
                    jobDetails.Id
                );
                _keepAliveService.StopPinging();
            }
        }

        public async Task<AnalysisResponseDto> PerformComparisonAnalysisAsync(
            ComparisonAnalysisRequestDto request,
            List<IFormFile> pdfFiles
        )
        {
            string promptFileName = request.ComparisonType switch
            {
                ComparisonType.Vendor => "vendor-comparison-prompt.pdf",
                ComparisonType.Subcontractor => "subcontractor-comparison-prompt.pdf",
                _ => throw new ArgumentException("Invalid comparison type"),
            };

            var prompt = await _promptManager.GetPromptAsync("ComparisonPrompts/", promptFileName);

            var combinedPdfText = new StringBuilder();
            foreach (var pdfFile in pdfFiles)
            {
                using var memoryStream = new MemoryStream();
                await pdfFile.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                var pdfText = await _pdfTextExtractionService.ExtractTextAsync(memoryStream);
                combinedPdfText.AppendLine(pdfText);
            }

            var fullPrompt = $"{prompt}\n\n{combinedPdfText}";

            var (analysisResult, conversationId) =
                await _aiService.StartMultimodalConversationAsync(
                    request.UserId,
                    null,
                    fullPrompt,
                    "Analyze the document based on the provided details."
                );

            return new AnalysisResponseDto
            {
                AnalysisResult = analysisResult,
                ConversationId = conversationId,
            };
        }

        public async Task<string> GenerateRebuttalAsync(string conversationId, string clientQuery)
        {
            var rebuttalPrompt =
                await _promptManager.GetPromptAsync("", "bid-justification-rebuttal-prompt.txt")
                + $"\n\n**Client Query to Address:**\n{clientQuery}";
            var (response, _) = await _aiService.ContinueConversationAsync(
                conversationId,
                "system-user",
                rebuttalPrompt,
                null,
                false
            );
            return response;
        }

        public async Task<string> GenerateRevisionAsync(
            string conversationId,
            string revisionRequest
        )
        {
            var revisionPrompt =
                await _promptManager.GetPromptAsync("", "prompt-revision.txt")
                + $"\n\n**Revision Request:**\n{revisionRequest}";
            var (response, _) = await _aiService.ContinueConversationAsync(
                conversationId,
                "system-user",
                revisionPrompt,
                null,
                false
            );
            return response;
        }

        private async Task<string> ExecuteSequentialPromptsAsync(
            string conversationId,
            string userId,
            string initialResponse,
            int jobId,
            string? connectionId,
            DateTime analysisStartedAtUtc
        )
        {
            _logger.LogInformation(
                "ExecuteSequentialPromptsAsync: instanceId={InstanceId} pid={Pid} jobId={JobId} conversationId={ConversationId}",
                _instanceId,
                System.Diagnostics.Process.GetCurrentProcess().Id,
                jobId,
                conversationId
            );
            var stringBuilder = new StringBuilder();
            initialResponse = EnsurePhaseHeading(initialResponse, 1, InitialAnalysisPhaseTitle);
            stringBuilder.Append(initialResponse);

            var promptNames = new[]
            {
                "prompt-01-site-logistics",
                "prompt-02-quality-management",
                "prompt-03-demolition",
                "prompt-04-groundwork",
                "prompt-05-framing",
                "prompt-06-roofing",
                "prompt-07-exterior",
                "prompt-08-electrical",
                "prompt-09-plumbing",
                "prompt-10-hvac",
                "prompt-11-fire-protection",
                "prompt-12-insulation",
                "prompt-13-drywall",
                "prompt-14-painting",
                "prompt-15-trim",
                "prompt-16-kitchen-bath",
                "prompt-17-flooring",
                "prompt-18-exterior-flatwork",
                "prompt-19-cleaning",
                "prompt-20-risk-analyst",
                "prompt-21-timeline",
                "prompt-22-general-conditions",
                "prompt-23-procurement",
                "prompt-24-daily-construction-plan",
                "prompt-25-cost-breakdowns",
                "prompt-26-value-engineering",
                "prompt-27-environmental-lifecycle",
                "prompt-28-project-closeout",
                "prompt-29-final-client-quotation-package",
                "executive-summary-prompt",
            };

            try
            {
                string lastResponse;
                var completedPrompts = await GetCompletedPromptNames(jobId);
                var savedModelPrompts = await GetSavedModelPromptNames(jobId);

                foreach (var promptName in savedModelPrompts)
                {
                    if (!completedPrompts.Contains(promptName))
                    {
                        // Do not mark completed just because the model response was saved.
                        // A prompt is only considered completed once we have persisted a JobPromptResult.
                        var hasPersisted = await _context.JobPromptResults.AsNoTracking()
                            .AnyAsync(r => r.JobId == jobId && r.PromptKey == promptName);
                        if (hasPersisted)
                        {
                            await MarkPromptCompleted(jobId, promptName);
                            completedPrompts.Add(promptName);
                        }
                    }
                }

                // Resume safety: if a prompt is marked completed but no JobPromptResult exists,
                // treat it as incomplete so the prompt reruns and persists.
                foreach (var promptName in promptNames)
                {
                    if (!completedPrompts.Contains(promptName) || IsScopeSnapshotPromptKey(promptName))
                    {
                        continue;
                    }

                    var hasPersisted = await _context.JobPromptResults.AsNoTracking()
                        .AnyAsync(r => r.JobId == jobId && r.PromptKey == promptName);
                    if (!hasPersisted)
                    {
                        _logger.LogWarning(
                            "Resume detected completed prompt without JobPromptResults row. Will rerun: jobId={JobId} promptKey={PromptKey}",
                            jobId,
                            promptName
                        );
                        completedPrompts.Remove(promptName);
                    }
                }

                int completedCount = promptNames.Count(p => completedPrompts.Contains(p));
                foreach (var promptName in promptNames)
                {
                    if (
                        completedPrompts.Contains(promptName)
                        && !IsScopeSnapshotPromptKey(promptName)
                    )
                    {
                        continue;
                    }

                    var progressStep = completedCount + 1;
                    var promptIndex = Array.IndexOf(promptNames, promptName);
                    var phaseNumber = promptIndex >= 0 ? promptIndex + 2 : progressStep;

                    _logger.LogInformation(
                        "Executing step {Step} of {TotalSteps}: {PromptName} for conversation {ConversationId}",
                        progressStep,
                        promptNames.Length,
                        promptName,
                        conversationId
                    );

                    await TrySendAnalysisProgressUpdate(
                        connectionId,
                        new Middleware.AnalysisProgressUpdate
                        {
                            JobId = jobId,
                            StatusMessage = $"Analyzing: {FormatPromptStatusLabel(promptName)}",
                            CurrentStep = progressStep,
                            TotalSteps = promptNames.Length,
                            IsComplete = false,
                            HasFailed = false,
                        }
                    );

                    await UpdateAnalysisState(
                        jobId,
                        $"Analyzing: {FormatPromptStatusLabel(promptName)}",
                        progressStep,
                        promptNames.Length
                    );

                    var promptText = await _promptManager.GetPromptAsync("", $"{promptName}.txt");
                    promptText = await ApplyDeterministicScopePromptInstructionsAsync(
                        jobId,
                        promptName,
                        promptText
                    );
                    var phaseTitle = FormatPromptStatusLabel(promptName);
                    var phasePrefix = BuildPhaseInstructionPrefix(phaseNumber, phaseTitle);
                    (lastResponse, _) = await _aiService.ContinueConversationAsync(
                        conversationId,
                        userId,
                        $"{phasePrefix}\n\n{promptText}",
                        null,
                        true
                    );
                    lastResponse = EnsurePhaseHeading(lastResponse, phaseNumber, phaseTitle);

                    await _conversationRepo.AddMessageIfNotExistsAsync(
                        new Message
                        {
                            ConversationId = conversationId,
                            Role = "model",
                            Content = lastResponse,
                        }
                    );

                    await MarkModelPromptSaved(jobId, promptName);

                    stringBuilder.Append("\n\n---\n\n");
                    stringBuilder.Append(lastResponse);

                    _logger.LogInformation(
                        "About to persist JobPromptResults: instanceId={InstanceId} pid={Pid} jobId={JobId} promptKey={PromptKey}",
                        _instanceId,
                        System.Diagnostics.Process.GetCurrentProcess().Id,
                        jobId,
                        promptName
                    );
                    var persisted = await ParseAndBroadcastPromptResult(
                        jobId,
                        promptName,
                        lastResponse,
                        connectionId
                    );
                    if (persisted)
                    {
                        var normalizedPromptKey = NormalizePromptKey(promptName);
                        var exists = await _context.JobPromptResults.AsNoTracking()
                            .AnyAsync(r => r.JobId == jobId && r.PromptKey == normalizedPromptKey);
                        if (!exists)
                        {
                            _logger.LogError(
                                "Persist pipeline returned persisted=true but no JobPromptResults row exists. jobId={JobId} promptKey={PromptKey}",
                                jobId,
                                normalizedPromptKey
                            );
                            persisted = false;
                        }
                    }
                    _logger.LogInformation(
                        "Finished persisting JobPromptResults: instanceId={InstanceId} pid={Pid} jobId={JobId} promptKey={PromptKey} persisted={Persisted}",
                        _instanceId,
                        System.Diagnostics.Process.GetCurrentProcess().Id,
                        jobId,
                        promptName,
                        persisted
                    );

                    if (persisted)
                    {
                        await MarkPromptCompleted(jobId, promptName);
                        completedCount++;
                    }
                }

                await EnsureScopeSnapshotCompletenessAsync(jobId, analysisStartedAtUtc);

                var job = await _context.Jobs.FindAsync(jobId);
                if (job != null)
                {
                    job.Status = "PRELIMINARY";
                    await _context.SaveChangesAsync();
                }

                await TrySendAnalysisProgressUpdate(
                    connectionId,
                    new Middleware.AnalysisProgressUpdate
                    {
                        JobId = jobId,
                        StatusMessage = "Analysis complete.",
                        CurrentStep = promptNames.Length,
                        TotalSteps = promptNames.Length,
                        IsComplete = true,
                        HasFailed = false,
                    }
                );

                _logger.LogInformation(
                    "Full sequential analysis completed successfully for conversation {ConversationId}",
                    conversationId
                );
                return await BuildFullReportFromConversation(conversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An error occurred during sequential prompt execution for conversation {ConversationId}",
                    conversationId
                );
                await TrySendAnalysisProgressUpdate(
                    connectionId,
                    new Middleware.AnalysisProgressUpdate
                    {
                        JobId = jobId,
                        StatusMessage = "Analysis failed during sequential execution.",
                        IsComplete = false,
                        HasFailed = true,
                        ErrorMessage = ex.Message,
                    }
                );
                throw;
            }
        }

        private async Task<string> BuildFullReportFromConversation(string conversationId)
        {
            var messages = await _conversationRepo.GetMessagesAsync(conversationId, includeSummarized: true);
            var modelMessages = messages
                .Where(m =>
                    string.Equals(m.Role, "model", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                )
                .Select(m => m.Content)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            if (!modelMessages.Any())
            {
                return string.Empty;
            }

            // Rebuild with server-authoritative sequential phase numbers so duplicate/missing
            // phase headings from resumed runs or model drift cannot leak into the final report.
            var normalizedPhaseMessages = new List<string>();
            var nonPhaseMessages = new List<string>();
            var phaseNumber = 1;

            foreach (var message in modelMessages)
            {
                if (TryExtractFirstPhaseTitle(message, out var phaseTitle))
                {
                    normalizedPhaseMessages.Add(EnsurePhaseHeading(message, phaseNumber, phaseTitle));
                    phaseNumber++;
                    continue;
                }

                if (phaseNumber == 1)
                {
                    normalizedPhaseMessages.Add(
                        EnsurePhaseHeading(message, phaseNumber, InitialAnalysisPhaseTitle)
                    );
                    phaseNumber++;
                    continue;
                }

                nonPhaseMessages.Add(message.Trim());
            }

            var orderedMessages = normalizedPhaseMessages
                .Concat(nonPhaseMessages.Where(m => !string.IsNullOrWhiteSpace(m)))
                .ToList();

            return string.Join("\n\n---\n\n", orderedMessages);
        }

        private async Task<HashSet<string>> GetCompletedPromptNames(int jobId)
        {
            var state = await _context.JobAnalysisStates.AsNoTracking()
                .FirstOrDefaultAsync(s => s.JobId == jobId);

            if (state == null || string.IsNullOrWhiteSpace(state.ExtractedDataJson))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(state.ExtractedDataJson);
                if (payload == null)
                {
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                if (!payload.TryGetValue("completedPrompts", out var raw) || raw == null)
                {
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                if (raw is JsonElement element && element.ValueKind == JsonValueKind.Array)
                {
                    var items = new List<string>();
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var value = item.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                items.Add(value);
                            }
                        }
                    }

                    return new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
            }

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<HashSet<string>> GetSavedModelPromptNames(int jobId)
        {
            var state = await _context.JobAnalysisStates.AsNoTracking()
                .FirstOrDefaultAsync(s => s.JobId == jobId);
            if (state == null || string.IsNullOrWhiteSpace(state.ExtractedDataJson))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(state.ExtractedDataJson);
                if (payload == null)
                {
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                if (!payload.TryGetValue("savedModelPrompts", out var raw) || raw == null)
                {
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                if (raw is JsonElement element && element.ValueKind == JsonValueKind.Array)
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var value = item.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                set.Add(value);
                            }
                        }
                    }
                    return set;
                }
            }
            catch
            {
            }

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private async Task MarkModelPromptSaved(int jobId, string promptName)
        {
            var state = await _context.JobAnalysisStates.AsNoTracking()
                .FirstOrDefaultAsync(s => s.JobId == jobId);

            var payload = new Dictionary<string, object>();
            if (state != null && !string.IsNullOrWhiteSpace(state.ExtractedDataJson))
            {
                try
                {
                    payload = JsonSerializer.Deserialize<Dictionary<string, object>>(state.ExtractedDataJson)
                        ?? new Dictionary<string, object>();
                }
                catch
                {
                    payload = new Dictionary<string, object>();
                }
            }

            var saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (payload.TryGetValue("savedModelPrompts", out var existing) && existing is JsonElement element && element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            saved.Add(value);
                        }
                    }
                }
            }

            saved.Add(promptName);
            var savedJson = JsonSerializer.Serialize(saved.ToArray());

            var rows = await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [JobAnalysisStates]
SET [ExtractedDataJson] = JSON_MODIFY(
        COALESCE(NULLIF([ExtractedDataJson], ''), '{{}}'),
        '$.savedModelPrompts',
        JSON_QUERY({savedJson})
    ),
    [LastUpdated] = SYSUTCDATETIME()
WHERE [JobId] = {jobId};
");

            if (rows == 0)
            {
                _context.JobAnalysisStates.Add(
                    new JobAnalysisState { JobId = jobId, ExtractedDataJson = "{}", LastUpdated = DateTime.UtcNow }
                );
                await _context.SaveChangesAsync();

                await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [JobAnalysisStates]
SET [ExtractedDataJson] = JSON_MODIFY(
        COALESCE(NULLIF([ExtractedDataJson], ''), '{{}}'),
        '$.savedModelPrompts',
        JSON_QUERY({savedJson})
    ),
    [LastUpdated] = SYSUTCDATETIME()
WHERE [JobId] = {jobId};
");
            }
        }

        private async Task MarkPromptCompleted(int jobId, string promptName)
        {
            var state = await _context.JobAnalysisStates.AsNoTracking()
                .FirstOrDefaultAsync(s => s.JobId == jobId);

            var payload = new Dictionary<string, object>();
            if (state != null && !string.IsNullOrWhiteSpace(state.ExtractedDataJson))
            {
                try
                {
                    payload = JsonSerializer.Deserialize<Dictionary<string, object>>(state.ExtractedDataJson)
                        ?? new Dictionary<string, object>();
                }
                catch
                {
                    payload = new Dictionary<string, object>();
                }
            }

            var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (payload.TryGetValue("completedPrompts", out var existing) && existing is JsonElement element && element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            completed.Add(value);
                        }
                    }
                }
            }

            completed.Add(promptName);
            var completedJson = JsonSerializer.Serialize(completed.ToArray());

            var rows = await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [JobAnalysisStates]
SET [ExtractedDataJson] = JSON_MODIFY(
        COALESCE(NULLIF([ExtractedDataJson], ''), '{{}}'),
        '$.completedPrompts',
        JSON_QUERY({completedJson})
    ),
    [LastUpdated] = SYSUTCDATETIME()
WHERE [JobId] = {jobId};
");

            if (rows == 0)
            {
                _context.JobAnalysisStates.Add(
                    new JobAnalysisState { JobId = jobId, ExtractedDataJson = "{}", LastUpdated = DateTime.UtcNow }
                );
                await _context.SaveChangesAsync();

                await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [JobAnalysisStates]
SET [ExtractedDataJson] = JSON_MODIFY(
        COALESCE(NULLIF([ExtractedDataJson], ''), '{{}}'),
        '$.completedPrompts',
        JSON_QUERY({completedJson})
    ),
    [LastUpdated] = SYSUTCDATETIME()
WHERE [JobId] = {jobId};
");
            }
        }

        private async Task<string> HandleFailureAsync(
            string conversationId,
            string userId,
            IEnumerable<string> documentUrls,
            string failedResponse
        )
        {
            _logger.LogInformation(
                "Failure prompt called for conversation {ConversationId}",
                conversationId
            );
            var correctivePrompt = await _promptManager.GetPromptAsync(
                null,
                FailureCorrectiveActionKey
            );
            var correctiveInput =
                $"{correctivePrompt}\n\nOriginal Failed Response:\n{failedResponse}";

            _logger.LogInformation("Calling ContinueConversationAsync for corrective action.");
            var (response, _) = await _aiService.ContinueConversationAsync(
                conversationId,
                userId,
                correctiveInput,
                null,
                true
            );

            return response;
        }

        private async Task<string> GetUserContextAsString(
            string userContextText,
            string userContextFileUrl
        )
        {
            var contextBuilder = new StringBuilder();
            if (
                !string.IsNullOrWhiteSpace(userContextText)
                && !userContextText.Contains("Analysis started with selected prompts:")
            )
            {
                contextBuilder.AppendLine("## User-Provided Context");
                contextBuilder.AppendLine(userContextText);
            }

            if (!string.IsNullOrWhiteSpace(userContextFileUrl))
            {
                try
                {
                    var (contentStream, _, _) = await _azureBlobService.GetBlobContentAsync(
                        userContextFileUrl
                    );
                    using var reader = new StreamReader(contentStream);
                    var fileContent = await reader.ReadToEndAsync();

                    if (!string.IsNullOrWhiteSpace(fileContent))
                    {
                        if (contextBuilder.Length == 0)
                        {
                            contextBuilder.AppendLine("## User-Provided Context");
                        }
                        contextBuilder.AppendLine("\n--- Context File Content ---");
                        contextBuilder.AppendLine(fileContent);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to read user context file from URL: {Url}",
                        userContextFileUrl
                    );
                }
            }

            return contextBuilder.ToString();
        }

        private async Task<bool> ParseAndBroadcastPromptResult(
            int jobId,
            string promptName,
            string response,
            string? connectionId
        )
        {
            var persisted = false;
            try
            {
                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning(
                        "ParseAndBroadcastPromptResult: Response is empty for prompt: {PromptName}",
                        promptName
                    );
                    return false;
                }

                persisted = await PersistJsonFirstPromptResult(jobId, promptName, response);

                _logger.LogInformation(
                    "ParseAndBroadcastPromptResult: Processing response for prompt: {PromptName} for Connection: {ConnectionId}",
                    promptName,
                    connectionId
                );

                if (promptName.Contains("initial-analysis") || promptName.Contains("prompt-00"))
                {
                    _logger.LogInformation(
                        "ParseAndBroadcastPromptResult: Attempting to parse initial analysis data (Rooms, Metadata, Permits, Blueprint Issues, Zoning)."
                    );

                    // 1. Parse Rooms
                    // Looking for | Room Name | Area (sqft) |
                    var roomRegex = new Regex(
                        @"\|\s*(?:\*\*)?Room Name(?:\*\*)?\s*\|\s*(?:\*\*)?Area(?:\*\*)?.*?(\n\n|###|$)",
                        RegexOptions.Singleline
                    );
                    var roomMatch = roomRegex.Match(response);
                    if (roomMatch.Success)
                    {
                        _logger.LogInformation(
                            "ParseAndBroadcastPromptResult: Rooms regex matched."
                        );
                        var rooms = new List<object>();
                        var lines = roomMatch.Value.Split('\n');
                        foreach (var line in lines)
                        {
                            if (
                                string.IsNullOrWhiteSpace(line)
                                || !line.Trim().StartsWith("|")
                                || line.Contains("Room Name")
                                || line.Contains("---")
                            )
                                continue;

                            var cols = line.Split('|')
                                .Select(c => c.Trim())
                                .Where(c => !string.IsNullOrEmpty(c))
                                .ToList();
                            if (cols.Count >= 2)
                            {
                                rooms.Add(new { name = cols[0], area = cols[1] });
                            }
                        }

                        if (rooms.Any())
                        {
                            _logger.LogInformation(
                                "ParseAndBroadcastPromptResult: Extracted {Count} rooms. Sending 'rooms' via SignalR.",
                                rooms.Count
                            );
                            if (!string.IsNullOrEmpty(connectionId))
                            {
                                await _hubContext
                                    .Clients.Client(connectionId)
                                    .SendAsync(
                                        "ReceiveAnalysisData",
                                        new
                                        {
                                            JobId = jobId,
                                            DataType = "rooms",
                                            Data = rooms,
                                        }
                                    );
                            }
                            await UpdateAnalysisData(jobId, "rooms", rooms);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "ParseAndBroadcastPromptResult: Rooms regex matched but no rooms were parsed from table."
                            );
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ParseAndBroadcastPromptResult: Rooms regex FAILED to match."
                        );
                    }

                    // 2. Parse Metadata
                    // Looking for | Category | Details |
                    var metadataRegex = new Regex(
                        @"\|\s*(?:\*\*)?Category(?:\*\*)?\s*\|\s*(?:\*\*)?Details(?:\*\*)?.*?(\n\n|###|$)",
                        RegexOptions.Singleline
                    );
                    var metaMatch = metadataRegex.Match(response);
                    if (metaMatch.Success)
                    {
                        _logger.LogInformation(
                            "ParseAndBroadcastPromptResult: Metadata regex matched."
                        );
                        var metadata = new Dictionary<string, string>();
                        var lines = metaMatch.Value.Split('\n');
                        foreach (var line in lines)
                        {
                            if (
                                string.IsNullOrWhiteSpace(line)
                                || !line.Trim().StartsWith("|")
                                || line.Contains("Category")
                                || line.Contains("---")
                            )
                                continue;

                            var cols = line.Split('|')
                                .Select(c => c.Trim())
                                .Where(c => !string.IsNullOrEmpty(c))
                                .ToList();
                            if (cols.Count >= 2)
                            {
                                var key = cols[0].Replace("**", "").Replace(":", "");
                                var value = cols[1];

                                // Map to frontend keys
                                if (key.Contains("Project Name"))
                                    metadata["projectName"] = value;
                                if (key.Contains("Address"))
                                    metadata["location"] = value;
                                if (key.Contains("Architect"))
                                    metadata["architect"] = value;
                                if (key.Contains("Engineer"))
                                    metadata["engineer"] = value;
                            }
                        }

                        if (metadata.Any())
                        {
                            _logger.LogInformation(
                                "ParseAndBroadcastPromptResult: Extracted {Count} metadata items. Sending 'metadata' via SignalR.",
                                metadata.Count
                            );
                            if (!string.IsNullOrEmpty(connectionId))
                            {
                                await _hubContext
                                    .Clients.Client(connectionId)
                                    .SendAsync(
                                        "ReceiveAnalysisData",
                                        new
                                        {
                                            JobId = jobId,
                                            DataType = "metadata",
                                            Data = metadata,
                                        }
                                    );
                            }
                            await UpdateAnalysisData(jobId, "metadata", metadata);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "ParseAndBroadcastPromptResult: Metadata regex matched but no metadata items were parsed."
                            );
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ParseAndBroadcastPromptResult: Metadata regex FAILED to match."
                        );
                    }

                    // 3. Parse Permits
                    var permitRegex = new Regex(
                        @"\|\s*(?:\*\*)?Permit Name(?:\*\*)?\s*\|\s*(?:\*\*)?Issuing Agency(?:\*\*)?\s*\|\s*(?:\*\*)?Requirements(?:\*\*)?.*?(\n\n|###|$)",
                        RegexOptions.Singleline
                    );
                    var permitMatch = permitRegex.Match(response);
                    if (permitMatch.Success)
                    {
                        _logger.LogInformation(
                            "ParseAndBroadcastPromptResult: Permits regex matched."
                        );
                        var permits = new List<object>();
                        var lines = permitMatch.Value.Split('\n');
                        int idCounter = 1;
                        foreach (var line in lines)
                        {
                            if (
                                string.IsNullOrWhiteSpace(line)
                                || !line.Trim().StartsWith("|")
                                || line.Contains("Permit Name")
                                || line.Contains("---")
                            )
                                continue;

                            var cols = line.Split('|')
                                .Select(c => c.Trim())
                                .Where(c => !string.IsNullOrEmpty(c))
                                .ToList();
                            if (cols.Count >= 2)
                            {
                                permits.Add(
                                    new
                                    {
                                        id = idCounter++.ToString(),
                                        name = cols[0],
                                        agency = cols[1],
                                        status = "required",
                                        file = (string?)null,
                                    }
                                );
                            }
                        }
                        if (permits.Any())
                        {
                            _logger.LogInformation(
                                "ParseAndBroadcastPromptResult: Extracted {Count} permits. Sending 'permits' via SignalR.",
                                permits.Count
                            );
                            if (!string.IsNullOrEmpty(connectionId))
                            {
                                await _hubContext
                                    .Clients.Client(connectionId)
                                    .SendAsync(
                                        "ReceiveAnalysisData",
                                        new
                                        {
                                            JobId = jobId,
                                            DataType = "permits",
                                            Data = permits,
                                        }
                                    );
                            }
                            await UpdateAnalysisData(jobId, "permits", permits);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "ParseAndBroadcastPromptResult: Permits regex matched but no permits were parsed."
                            );
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ParseAndBroadcastPromptResult: Permits regex FAILED to match."
                        );
                    }

                    // 4. Parse Blueprint Issues
                    var issuesRegex = new Regex(
                        @"\|\s*(?:\*\*)?Sheet Number(?:\*\*)?\s*\|\s*(?:\*\*)?Error Description(?:\*\*)?\s*\|\s*(?:\*\*)?Potential Impact(?:\*\*)?.*?(\n\n|###|$)",
                        RegexOptions.Singleline
                    );
                    var issuesMatch = issuesRegex.Match(response);
                    if (issuesMatch.Success)
                    {
                        _logger.LogInformation(
                            "ParseAndBroadcastPromptResult: Blueprint Issues regex matched."
                        );
                        var issues = new List<object>();
                        var lines = issuesMatch.Value.Split('\n');
                        int idCounter = 1;
                        foreach (var line in lines)
                        {
                            if (
                                string.IsNullOrWhiteSpace(line)
                                || !line.Trim().StartsWith("|")
                                || line.Contains("Sheet Number")
                                || line.Contains("---")
                            )
                                continue;

                            var cols = line.Split('|')
                                .Select(c => c.Trim())
                                .Where(c => !string.IsNullOrEmpty(c))
                                .ToList();
                            if (cols.Count >= 2)
                            {
                                // Simple logic to determine type/severity from text keywords
                                var desc = cols[1];
                                var type = desc.ToLower().Contains("missing") ? "error" : "warning";

                                issues.Add(
                                    new
                                    {
                                        id = $"issue-{idCounter++}",
                                        type = type,
                                        title = "Blueprint Issue", // Or extract from desc
                                        message = desc,
                                        code = "Review",
                                        location = cols[0],
                                        details = desc,
                                        recommendation = "Review with architect",
                                        severity = type == "error" ? "critical" : "medium",
                                        estimatedImpact = cols.Count > 2 ? cols[2] : "Unknown",
                                    }
                                );
                            }
                        }

                        if (issues.Any())
                        {
                            _logger.LogInformation(
                                "ParseAndBroadcastPromptResult: Extracted {Count} blueprint issues. Sending 'blueprint-issues' via SignalR.",
                                issues.Count
                            );
                            if (!string.IsNullOrEmpty(connectionId))
                            {
                                await _hubContext
                                    .Clients.Client(connectionId)
                                    .SendAsync(
                                        "ReceiveAnalysisData",
                                        new
                                        {
                                            JobId = jobId,
                                            DataType = "blueprint-issues",
                                            Data = issues,
                                        }
                                    );
                            }
                            await UpdateAnalysisData(jobId, "blueprint-issues", issues);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "ParseAndBroadcastPromptResult: Blueprint Issues regex matched but no issues were parsed."
                            );
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ParseAndBroadcastPromptResult: Blueprint Issues regex FAILED to match."
                        );
                    }

                    // 5. Parse Zoning (Real data extraction)
                    var zoningRegex = new Regex(
                        @"#{1,6}\s*(?:\*\*)?.*?Zoning & Site Report(?:\*\*)?([\s\S]*?)(?:#{1,6}|$)",
                        RegexOptions.Singleline
                    );
                    var zoningMatch = zoningRegex.Match(response);
                    if (zoningMatch.Success)
                    {
                        _logger.LogInformation(
                            "ParseAndBroadcastPromptResult: Zoning regex matched."
                        );
                        var zoningContent = zoningMatch.Groups[1].Value;
                        var zoningData = new Dictionary<string, object>
                        {
                            { "zoning", "See Report" },
                            { "lotSize", "See Report" },
                            { "setbacks", new Dictionary<string, string>() },
                            { "utilities", new List<string>() },
                            { "unforeseenWork", new List<object>() },
                        };

                        // Extract Zoning Text
                        var zoningComplianceMatch = Regex.Match(
                            zoningContent,
                            @"\*\*Zoning Compliance:\*\*(.*?)\n",
                            RegexOptions.Singleline
                        );
                        if (zoningComplianceMatch.Success)
                        {
                            var text = zoningComplianceMatch.Groups[1].Value.Trim();
                            if (text.Length > 100)
                                zoningData["zoning"] = text.Substring(0, 97) + "...";
                            else
                                zoningData["zoning"] = text;
                        }

                        // Extract Lot Size
                        var lotSizeMatch = Regex.Match(
                            zoningContent,
                            @"(\d{1,3}(,\d{3})*(\.\d+)?) sq ft lot"
                        );
                        if (lotSizeMatch.Success)
                        {
                            zoningData["lotSize"] = lotSizeMatch.Value;
                        }

                        // Extract Utilities
                        var utilMatch = Regex.Match(
                            zoningContent,
                            @"(?:\*\*)?Utility Status(?:\*\*)?:(.*?)(?=\n\*|\n#|$)",
                            RegexOptions.Singleline
                        );
                        if (utilMatch.Success)
                        {
                            var utils = Regex.Matches(utilMatch.Value, @"\*\*(\w+):\*\*");
                            foreach (Match u in utils)
                            {
                                ((List<string>)zoningData["utilities"]).Add(u.Groups[1].Value);
                            }
                        }

                        // Extract Setbacks
                        var setbackMatch = Regex.Match(zoningContent, @"(\d+)' building line");
                        if (setbackMatch.Success)
                        {
                            var setbacks = Regex.Matches(
                                zoningContent,
                                @"(\d+)' building line on the (.*?) side"
                            );
                            var setbackDict = (Dictionary<string, string>)zoningData["setbacks"];
                            foreach (Match s in setbacks)
                            {
                                var side = s.Groups[2].Value.Trim();
                                var dist = s.Groups[1].Value.Trim() + " ft";
                                setbackDict[side] = dist;
                            }
                        }

                        // Extract Unforeseen Work
                        var riskSectionMatch = Regex.Match(
                            zoningContent,
                            @"(?:\*\*)?Unforeseen Work(?:\*\*)?.*?:(.*?)(?=\n\*|\n#|$)",
                            RegexOptions.Singleline
                        );
                        if (riskSectionMatch.Success)
                        {
                            var text = riskSectionMatch.Groups[1].Value.Trim();
                            var risks = text.Split(
                                new[] { ". " },
                                StringSplitOptions.RemoveEmptyEntries
                            );
                            foreach (var risk in risks)
                            {
                                if (risk.Trim().Length > 0)
                                {
                                    ((List<object>)zoningData["unforeseenWork"]).Add(
                                        new { item = risk.Trim(), risk = "medium" }
                                    );
                                }
                            }
                        }

                        _logger.LogInformation(
                            "ParseAndBroadcastPromptResult: Sending 'zoning' via SignalR."
                        );
                        if (!string.IsNullOrEmpty(connectionId))
                        {
                            await _hubContext
                                .Clients.Client(connectionId)
                                .SendAsync(
                                    "ReceiveAnalysisData",
                                    new
                                    {
                                        JobId = jobId,
                                        DataType = "zoning",
                                        Data = zoningData,
                                    }
                                );
                        }
                        await UpdateAnalysisData(jobId, "zoning", zoningData);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ParseAndBroadcastPromptResult: Zoning regex FAILED to match."
                        );
                    }
                }

                if (promptName.Contains("site-logistics") || promptName.Contains("prompt-01"))
                {
                    _logger.LogInformation(
                        "ParseAndBroadcastPromptResult: Attempting to parse Site Logistics."
                    );

                    // Parse Site Logistics
                    var logistics = new Dictionary<string, List<string>>
                    {
                        { "zones", new List<string>() },
                        { "equipment", new List<string>() },
                        { "hazards", new List<string>() },
                        { "ppe", new List<string>() },
                    };

                    // Simple extraction based on keywords/lists
                    var zonesMatch = Regex.Match(
                        response,
                        @"(Zones|Laydown Areas).*?(\n\* .*?)+",
                        RegexOptions.Singleline
                    );
                    if (zonesMatch.Success)
                    {
                        var matches = Regex
                            .Matches(zonesMatch.Value, @"\* \*\*(.*?)\*\*")
                            .Select(m => m.Groups[1].Value)
                            .ToList();
                        if (!matches.Any())
                            matches = Regex
                                .Matches(zonesMatch.Value, @"\* (.*)")
                                .Select(m => m.Groups[1].Value)
                                .ToList();
                        logistics["zones"].AddRange(matches);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ParseAndBroadcastPromptResult: Logistics Zones/Laydown match failed."
                        );
                    }

                    var equipmentMatch = Regex.Match(
                        response,
                        @"(Equipment).*?(\n\* .*?)+",
                        RegexOptions.Singleline
                    );
                    if (equipmentMatch.Success)
                    {
                        var matches = Regex
                            .Matches(equipmentMatch.Value, @"\* \*\*(.*?)\*\*")
                            .Select(m => m.Groups[1].Value)
                            .ToList();
                        logistics["equipment"].AddRange(matches);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ParseAndBroadcastPromptResult: Logistics Equipment match failed."
                        );
                    }

                    var hazardMatch = Regex.Match(
                        response,
                        @"(Hazard).*?(\n\* .*?)+",
                        RegexOptions.Singleline
                    );
                    if (hazardMatch.Success)
                    {
                        var matches = Regex
                            .Matches(hazardMatch.Value, @"\* \*\*(.*?)\*\*")
                            .Select(m => m.Groups[1].Value)
                            .ToList();
                        logistics["hazards"].AddRange(matches);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ParseAndBroadcastPromptResult: Logistics Hazard match failed."
                        );
                    }

                    // PPE Table
                    var ppeRegex = new Regex(
                        @"\|\s*(?:\*\*)?PPE Item(?:\*\*)?\s*\|.*?(\n\n|###|$)",
                        RegexOptions.Singleline
                    );
                    var ppeMatch = ppeRegex.Match(response);
                    if (ppeMatch.Success)
                    {
                        var lines = ppeMatch.Value.Split('\n');
                        foreach (var line in lines)
                        {
                            if (
                                line.Contains("|")
                                && !line.Contains("---")
                                && !line.Contains("PPE Item")
                            )
                            {
                                var cols = line.Split('|');
                                if (cols.Length > 1)
                                    logistics["ppe"].Add(cols[1].Trim());
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ParseAndBroadcastPromptResult: Logistics PPE match failed."
                        );
                    }

                    _logger.LogInformation(
                        "ParseAndBroadcastPromptResult: Sending 'site-logistics' via SignalR."
                    );
                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        await _hubContext
                            .Clients.Client(connectionId)
                            .SendAsync(
                                "ReceiveAnalysisData",
                                new
                                {
                                    JobId = jobId,
                                    DataType = "site-logistics",
                                    Data = logistics,
                                }
                            );
                    }
                    await UpdateAnalysisData(jobId, "site-logistics", logistics);
                }

                if (promptName.Contains("quality-management") || promptName.Contains("prompt-02"))
                {
                    _logger.LogInformation(
                        "ParseAndBroadcastPromptResult: Attempting to parse Quality Management."
                    );

                    // Parse Quality Management
                    var quality = new Dictionary<string, List<string>>
                    {
                        { "codes", new List<string>() },
                        { "mockups", new List<string>() },
                        { "holdPoints", new List<string>() },
                    };

                    // Codes
                    var codesMatch = Regex.Match(
                        response,
                        @"(Codes|Standards).*?(\n\* .*?)+",
                        RegexOptions.Singleline
                    );
                    if (codesMatch.Success)
                    {
                        var codes = Regex.Matches(codesMatch.Value, @"[A-Z]{3,}\s\d{4}"); // Match things like IRC 2015
                        var codeList = codes.Select(m => m.Value).Distinct().ToList();
                        quality["codes"].AddRange(codeList);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ParseAndBroadcastPromptResult: Quality Codes/Standards match failed."
                        );
                    }

                    // Mockups Table
                    var mockupRegex = new Regex(
                        @"\|\s*(?:\*\*)?Mock-up Description(?:\*\*)?\s*\|.*?(\n\n|###|$)",
                        RegexOptions.Singleline
                    );
                    var mockupMatch = mockupRegex.Match(response);
                    if (mockupMatch.Success)
                    {
                        var lines = mockupMatch.Value.Split('\n');
                        foreach (var line in lines)
                        {
                            if (
                                line.Contains("|")
                                && !line.Contains("---")
                                && !line.Contains("Mock-up")
                            )
                            {
                                var cols = line.Split('|');
                                if (cols.Length > 1)
                                    quality["mockups"].Add(cols[1].Trim());
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ParseAndBroadcastPromptResult: Quality Mockups match failed."
                        );
                    }

                    // Hold Points (Inspection Table)
                    var holdRegex = new Regex(
                        @"\|\s*(?:\*\*)?Phase(?:\*\*)?\s*\|\s*(?:\*\*)?Activity(?:\*\*)?.*?Hold Point\?.*?(\n\n|###|$)",
                        RegexOptions.Singleline
                    );
                    var holdMatch = holdRegex.Match(response);
                    if (holdMatch.Success)
                    {
                        var lines = holdMatch.Value.Split('\n');
                        foreach (var line in lines)
                        {
                            if (
                                line.Contains("|")
                                && !line.Contains("---")
                                && !line.Contains("Phase")
                            )
                            {
                                var cols = line.Split('|');
                                if (cols.Length > 2 && cols.Last().ToUpper().Contains("Y")) // Hold Point column
                                {
                                    quality["holdPoints"].Add(cols[2].Trim());
                                }
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ParseAndBroadcastPromptResult: Quality Hold Points match failed."
                        );
                    }

                    _logger.LogInformation(
                        "ParseAndBroadcastPromptResult: Sending 'quality-management' via SignalR."
                    );
                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        await _hubContext
                            .Clients.Client(connectionId)
                            .SendAsync(
                                "ReceiveAnalysisData",
                                new
                                {
                                    JobId = jobId,
                                    DataType = "quality-management",
                                    Data = quality,
                                }
                            );
                    }
                    await UpdateAnalysisData(jobId, "quality-management", quality);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    $"Error parsing and broadcasting prompt result for {promptName}"
                );
                return false;
            }

            return persisted;
        }

        private async Task<bool> PersistJsonFirstPromptResult(int jobId, string promptName, string response)
        {
            try
            {
                var normalizedPromptKey = NormalizePromptKey(promptName);

                _logger.LogInformation(
                    "PersistJsonFirstPromptResult: instanceId={InstanceId} pid={Pid} jobId={JobId} promptKey={PromptKey}",
                    _instanceId,
                    System.Diagnostics.Process.GetCurrentProcess().Id,
                    jobId,
                    normalizedPromptKey
                );

                await EnsureJobPromptResultsTableAsync();

                if (Interlocked.Exchange(ref _jobPromptResultsTableDiagnosticsLogged, 1) == 0)
                {
                    try
                    {
                        var dbConn = _context.Database.GetDbConnection();
                        _logger.LogInformation(
                            "JobPromptResults diagnostics: DataSource={DataSource} Database={Database}",
                            dbConn.DataSource,
                            dbConn.Database
                        );

                        var wasClosed = dbConn.State != System.Data.ConnectionState.Open;
                        if (wasClosed)
                        {
                            await dbConn.OpenAsync();
                        }

                        using var cmd = dbConn.CreateCommand();
                        cmd.CommandText = @"
SELECT TOP 1 TABLE_TYPE
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME = 'JobPromptResults';";
                        var tableTypeObj = await cmd.ExecuteScalarAsync();
                        _logger.LogInformation(
                            "JobPromptResults diagnostics: INFORMATION_SCHEMA.TABLES.TABLE_TYPE={TableType}",
                            tableTypeObj?.ToString() ?? "<null>"
                        );

                        if (wasClosed)
                        {
                            await dbConn.CloseAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "JobPromptResults diagnostics failed.");
                    }
                }

                _logger.LogInformation(
                    "PersistJsonFirstPromptResult: jobId={JobId} promptName={PromptName} normalizedPromptKey={PromptKey}",
                    jobId,
                    promptName,
                    normalizedPromptKey
                );

                // Persist every prompt response so JobPromptResults stays in-sync with Messages.
                // Only scope snapshot prompts are required to contain valid JSON; all others will be
                // stored as raw responses with ParsedJson left null.
                var isScopeSnapshotPrompt = IsScopeSnapshotPromptKey(normalizedPromptKey)
                    || string.Equals(
                        normalizedPromptKey,
                        "prompt-00-initial-analysis",
                        StringComparison.OrdinalIgnoreCase
                    );

                if (!isScopeSnapshotPrompt)
                {
                    _context.JobPromptResults.Add(
                        new Models.JobPromptResult
                        {
                            JobId = jobId,
                            PromptKey = normalizedPromptKey,
                            SchemaVersion = null,
                            RawResponse = response,
                            ParsedJson = null,
                            CreatedAt = DateTime.UtcNow,
                        }
                    );
                    var rows = await _context.SaveChangesAsync();
                    _logger.LogInformation(
                        "PersistJsonFirstPromptResult: saved raw response (non-snapshot prompt). SaveChanges wrote {Rows} row(s) for jobId={JobId} promptKey={PromptKey}",
                        rows,
                        jobId,
                        normalizedPromptKey
                    );
                    return true;
                }

                var extractedJson = ExtractFirstJsonCodeBlockOrNull(response);
                if (string.IsNullOrWhiteSpace(extractedJson))
                {
                    // Still persist the raw response so we can debug schema drift.
                    var reason = "No parseable JSON object returned by model.";
                    _logger.LogWarning(
                        "PersistJsonFirstPromptResult: no JSON extracted for jobId={JobId} promptKey={PromptKey}. Persisting rejected raw response.",
                        jobId,
                        normalizedPromptKey
                    );
                    _context.JobPromptResults.Add(
                        new Models.JobPromptResult
                        {
                            JobId = jobId,
                            PromptKey = normalizedPromptKey,
                            SchemaVersion = null,
                            RawResponse = BuildRejectedRawResponse(response, reason),
                            ParsedJson = null,
                            CreatedAt = DateTime.UtcNow,
                        }
                    );
                    var rows = await _context.SaveChangesAsync();
                    _logger.LogInformation(
                        "PersistJsonFirstPromptResult: SaveChanges wrote {Rows} row(s) for jobId={JobId} promptKey={PromptKey}",
                        rows,
                        jobId,
                        normalizedPromptKey
                    );
                    _logger.LogInformation(
                        "PersistJsonFirstPromptResult: saved rejected raw response for jobId={JobId} promptKey={PromptKey}",
                        jobId,
                        normalizedPromptKey
                    );
                    return true;
                }

                var schemaVersion = TryReadSchemaVersion(extractedJson);
                if (
                    !TryValidateScopePromptJson(
                        promptName,
                        extractedJson,
                        schemaVersion,
                        out var validationError
                    )
                )
                {
                    _logger.LogWarning(
                        "PersistJsonFirstPromptResult: validation failed for jobId={JobId} prompt={PromptName}. Reason={Reason}",
                        jobId,
                        normalizedPromptKey,
                        validationError
                    );
                    _context.JobPromptResults.Add(
                        new Models.JobPromptResult
                        {
                            JobId = jobId,
                            PromptKey = normalizedPromptKey,
                            SchemaVersion = schemaVersion,
                            RawResponse = BuildRejectedRawResponse(response, validationError),
                            ParsedJson = null,
                            CreatedAt = DateTime.UtcNow,
                        }
                    );
                    var rows = await _context.SaveChangesAsync();
                    _logger.LogInformation(
                        "PersistJsonFirstPromptResult: SaveChanges wrote {Rows} row(s) for jobId={JobId} promptKey={PromptKey}",
                        rows,
                        jobId,
                        normalizedPromptKey
                    );
                    _logger.LogInformation(
                        "PersistJsonFirstPromptResult: saved rejected response for jobId={JobId} promptKey={PromptKey} schemaVersion={SchemaVersion}",
                        jobId,
                        normalizedPromptKey,
                        schemaVersion
                    );
                    return true;
                }

                var appliedPrompt25Anchors = false;
                if (
                    string.Equals(
                        normalizedPromptKey,
                        "prompt-25-cost-breakdowns",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    var directAnchors = await GetPrompt04To19DirectTotalsAsync(jobId);
                    if (directAnchors.hasTotals)
                    {
                        if (
                            TryReadCostBreakdownDirectTotalsFromJson(
                                extractedJson,
                                out var currentMaterialTotal,
                                out var currentLaborTotal
                            )
                        )
                        {
                            var materialMatches = IsWithinDirectTotalsTolerance(
                                directAnchors.materialTotal,
                                currentMaterialTotal
                            );
                            var laborMatches = IsWithinDirectTotalsTolerance(
                                directAnchors.laborTotal,
                                currentLaborTotal
                            );

                            if (!materialMatches || !laborMatches)
                            {
                                var reason =
                                    "Direct totals mismatch vs prompt-04..19 anchors (non-blocking). "
                                    + $"expectedMaterial={directAnchors.materialTotal:F2}, actualMaterial={currentMaterialTotal:F2}; "
                                    + $"expectedLabor={directAnchors.laborTotal:F2}, actualLabor={currentLaborTotal:F2}.";
                                _logger.LogWarning(
                                    "PersistJsonFirstPromptResult: cost_breakdowns.v1 mismatch for JobId={JobId}. {Reason}",
                                    jobId,
                                    reason
                                );
                            }
                        }

                        if (
                            TryApplyPrompt25DirectTotalsAnchors(
                                extractedJson,
                                directAnchors.materialTotal,
                                directAnchors.laborTotal,
                                out var harmonizedJson
                            )
                        )
                        {
                            extractedJson = harmonizedJson;
                            appliedPrompt25Anchors = true;
                        }
                    }

                    var priorParsedCostJson = await _context
                        .JobPromptResults.AsNoTracking()
                        .Where(
                            r =>
                                r.JobId == jobId
                                && (
                                    r.PromptKey == "prompt-25-cost-breakdowns"
                                    || r.PromptKey == "prompt-25-cost-breakdowns.txt"
                                )
                                && r.ParsedJson != null
                        )
                        .OrderByDescending(r => r.CreatedAt)
                        .Select(r => r.ParsedJson)
                        .FirstOrDefaultAsync();

                    if (
                        !appliedPrompt25Anchors
                        && (
                        TryGetGrandTotalBidPrice(priorParsedCostJson, out var priorTotal)
                        && TryGetGrandTotalBidPrice(extractedJson, out var currentTotal)
                        && priorTotal > 0
                        )
                    )
                    {
                        var pctDelta = Math.Abs(currentTotal - priorTotal) / priorTotal;
                        if (pctDelta > ScopeCostVarianceRejectThreshold)
                        {
                            _logger.LogWarning(
                                "PersistJsonFirstPromptResult: rejected cost_breakdowns.v1 due to high variance for JobId={JobId}. Previous={PreviousTotal}, Current={CurrentTotal}, DeltaPct={DeltaPct}",
                                jobId,
                                priorTotal,
                                currentTotal,
                                pctDelta
                            );
                            _context.JobPromptResults.Add(
                                new Models.JobPromptResult
                                {
                                    JobId = jobId,
                                    PromptKey = normalizedPromptKey,
                                    SchemaVersion = schemaVersion,
                                    RawResponse = BuildRejectedRawResponse(
                                        response,
                                        $"High variance vs previous accepted run ({pctDelta:P1} > {ScopeCostVarianceRejectThreshold:P0})."
                                    ),
                                    ParsedJson = null,
                                    CreatedAt = DateTime.UtcNow,
                                }
                            );
                            var rows = await _context.SaveChangesAsync();
                            _logger.LogInformation(
                                "PersistJsonFirstPromptResult: SaveChanges wrote {Rows} row(s) for jobId={JobId} promptKey={PromptKey}",
                                rows,
                                jobId,
                                normalizedPromptKey
                            );
                            _logger.LogInformation(
                                "PersistJsonFirstPromptResult: saved rejected high-variance response for jobId={JobId} promptKey={PromptKey} schemaVersion={SchemaVersion}",
                                jobId,
                                normalizedPromptKey,
                                schemaVersion
                            );
                            return true;
                        }
                    }
                }

                _context.JobPromptResults.Add(
                    new Models.JobPromptResult
                    {
                        JobId = jobId,
                        PromptKey = normalizedPromptKey,
                        SchemaVersion = schemaVersion,
                        RawResponse = response,
                        ParsedJson = extractedJson,
                        CreatedAt = DateTime.UtcNow,
                    }
                );
                var savedRows = await _context.SaveChangesAsync();
                _logger.LogInformation(
                    "PersistJsonFirstPromptResult: SaveChanges wrote {Rows} row(s) for jobId={JobId} promptKey={PromptKey}",
                    savedRows,
                    jobId,
                    normalizedPromptKey
                );

                _logger.LogInformation(
                    "PersistJsonFirstPromptResult: saved parsed JSON for jobId={JobId} promptKey={PromptKey} schemaVersion={SchemaVersion}",
                    jobId,
                    normalizedPromptKey,
                    schemaVersion
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "PersistJsonFirstPromptResult failed for jobId={JobId} promptName={PromptName}",
                    jobId,
                    promptName
                );
                return false;
            }
        }

        private async Task EnsureJobPromptResultsTableAsync()
        {
            if (Interlocked.Exchange(ref _jobPromptResultsTableEnsured, 1) != 0)
            {
                return;
            }

            try
            {
                // If the table already exists, do nothing.
                // If it does not exist, attempt to create it with the schema expected by EF.
                // This is intentionally defensive because migrations may not have been applied in some environments.
                var conn = _context.Database.GetDbConnection();
                var openedHere = conn.State != System.Data.ConnectionState.Open;
                if (openedHere)
                {
                    await conn.OpenAsync();
                }

                try
                {
                    string? tableType;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT TOP 1 TABLE_TYPE
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME = 'JobPromptResults';";
                        tableType = (await cmd.ExecuteScalarAsync())?.ToString();
                    }

                    if (string.Equals(tableType, "BASE TABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    if (string.Equals(tableType, "VIEW", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogError(
                            "JobPromptResults exists as a VIEW, not a table. Inserts will fail until the view is replaced with a table."
                        );
                        return;
                    }

                    _logger.LogWarning(
                        "JobPromptResults table not found (INFORMATION_SCHEMA.TABLES returned {TableType}). Attempting to create it.",
                        tableType ?? "<null>"
                    );

                    using (var create = conn.CreateCommand())
                    {
                        create.CommandText = @"
IF OBJECT_ID(N'[dbo].[JobPromptResults]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobPromptResults] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_JobPromptResults] PRIMARY KEY,
        [JobId] INT NOT NULL,
        [PromptKey] NVARCHAR(450) NOT NULL,
        [SchemaVersion] NVARCHAR(50) NULL,
        [RawResponse] NVARCHAR(MAX) NOT NULL,
        [ParsedJson] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2 NOT NULL
    );

    CREATE INDEX [IX_JobPromptResults_JobId] ON [dbo].[JobPromptResults]([JobId]);
    CREATE INDEX [IX_JobPromptResults_JobId_PromptKey_CreatedAt] ON [dbo].[JobPromptResults]([JobId], [PromptKey], [CreatedAt]);
END";
                        await create.ExecuteNonQueryAsync();
                    }

                    _logger.LogInformation("JobPromptResults table ensured/created successfully.");
                }
                finally
                {
                    if (openedHere)
                    {
                        await conn.CloseAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                // Do not throw: analysis can still proceed, but inserts will fail and will be logged where they happen.
                _logger.LogError(ex, "Failed to ensure/create JobPromptResults table.");
            }
        }

        private static string? ExtractFirstJsonCodeBlockOrNull(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return null;
            }

            var fenceRegex = new Regex(@"```json\b", RegexOptions.IgnoreCase);
            var fenceMatch = fenceRegex.Match(response);
            if (!fenceMatch.Success)
            {
                return NormalizeJsonOrNull(response);
            }

            var afterFence = response.Substring(fenceMatch.Index + fenceMatch.Length);
            var startObj = afterFence.IndexOf('{');
            if (startObj < 0)
            {
                return null;
            }

            var slice = afterFence.Substring(startObj);
            var extracted = TryExtractBalancedJsonObject(slice);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return NormalizeJsonOrNull(extracted);
            }

            // If the balanced scan fails (common with truncated model output), attempt to normalize
            // and parse the full slice before giving up.
            var normalizedSlice = NormalizeJsonOrNull(slice);
            if (!string.IsNullOrWhiteSpace(normalizedSlice))
            {
                return normalizedSlice;
            }

            // Best-effort fallback: if the model output is missing the closing fence or has trailing text,
            // trim to the last closing brace and attempt to parse.
            var lastBrace = slice.LastIndexOf('}');
            if (lastBrace > 0)
            {
                var trimmed = slice.Substring(0, lastBrace + 1);
                return NormalizeJsonOrNull(trimmed);
            }

            return null;
        }

        private static string? NormalizeJsonOrNull(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            // Some LLM outputs mistakenly use SQL-style NULL; normalize to JSON null.
            var normalized = Regex.Replace(json, @":\s*NULL\b", ": null", RegexOptions.IgnoreCase);

            // Some outputs omit the value for flagsRequiresReview (e.g. "flagsRequiresReview": ,)
            // Default it to an empty array so the JSON remains valid.
            normalized = Regex.Replace(
                normalized,
                "\"flagsRequiresReview\"\\s*:\\s*(?=,|}|$)",
                "\"flagsRequiresReview\": []",
                RegexOptions.IgnoreCase
            );

            // Remove trailing commas before closing braces/brackets.
            normalized = Regex.Replace(normalized, @",\s*(\}|\])", "$1");

            // If the JSON is truncated (common when the model stops mid-object), try to close any
            // missing braces so JsonDocument can parse. Only append closing braces, never remove content.
            normalized = TryCloseUnbalancedJsonBraces(normalized);

            try
            {
                using var doc = JsonDocument.Parse(normalized);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }
                return normalized;
            }
            catch
            {
                return null;
            }
        }

        private static string TryCloseUnbalancedJsonBraces(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var depth = 0;
            var inString = false;
            var escape = false;

            foreach (var c in text)
            {
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth = Math.Max(0, depth - 1);
                }
            }

            if (depth <= 0)
            {
                return text;
            }

            // Cap the number of braces we will append to avoid runaway behavior on totally invalid content.
            var toAppend = Math.Min(depth, 20);
            return text + new string('}', toAppend);
        }

        private static string? TryExtractBalancedJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var depth = 0;
            var inString = false;
            var escape = false;
            var started = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (!started)
                {
                    if (c != '{')
                    {
                        continue;
                    }
                    started = true;
                    depth = 1;
                    continue;
                }

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                    continue;
                }

                if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(0, i + 1);
                    }
                }
            }

            return null;
        }

        private static string? TryReadSchemaVersion(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (doc.RootElement.TryGetProperty("schemaVersion", out var schemaEl))
                {
                    return schemaEl.ValueKind == JsonValueKind.String ? schemaEl.GetString() : null;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<int> BackfillJobPromptResultsParsedJsonAsync(
            int jobId,
            IEnumerable<string>? promptKeys = null
        )
        {
            var keys = promptKeys?.ToList();

            var query = _context.JobPromptResults.Where(r => r.JobId == jobId);
            if (keys != null && keys.Count > 0)
            {
                query = query.Where(r => keys.Contains(r.PromptKey));
            }

            var rows = await query.Where(r => r.ParsedJson == null).ToListAsync();
            if (rows.Count == 0)
            {
                return 0;
            }

            var updated = 0;
            foreach (var row in rows)
            {
                var extracted = ExtractFirstJsonCodeBlockOrNull(row.RawResponse);
                if (string.IsNullOrWhiteSpace(extracted))
                {
                    continue;
                }

                var schemaVersion = TryReadSchemaVersion(extracted);
                if (
                    !TryValidateScopePromptJson(
                        row.PromptKey,
                        extracted,
                        schemaVersion,
                        out var validationError
                    )
                )
                {
                    _logger.LogWarning(
                        "BackfillJobPromptResultsParsedJsonAsync: validation failed for rowId={RowId} jobId={JobId} prompt={Prompt}. Reason={Reason}",
                        row.Id,
                        row.JobId,
                        row.PromptKey,
                        validationError
                    );
                    continue;
                }

                row.ParsedJson = extracted;
                row.SchemaVersion ??= schemaVersion;
                updated++;
            }

            if (updated > 0)
            {
                await _context.SaveChangesAsync();
            }

            return updated;
        }

        public async Task ParseAndSaveAiJobDetails(int jobId, string aiResponse)
        {
            try
            {
                var jsonRegex = new Regex(@"```json\s*(\{.*?\})\s*```", RegexOptions.Singleline);
                var match = jsonRegex.Match(aiResponse);
                if (match.Success)
                {
                    var json = match.Groups[1].Value;
                    var extractedData = JsonSerializer.Deserialize<JobModel>(
                        json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    var jobToUpdate = await _context.Jobs.FindAsync(jobId);
                    if (jobToUpdate != null && extractedData != null)
                    {
                        jobToUpdate.ProjectName =
                            extractedData.ProjectName ?? jobToUpdate.ProjectName;
                        jobToUpdate.WallStructure =
                            extractedData.WallStructure ?? jobToUpdate.WallStructure;
                        jobToUpdate.RoofStructure =
                            extractedData.RoofStructure ?? jobToUpdate.RoofStructure;
                        jobToUpdate.Foundation = extractedData.Foundation ?? jobToUpdate.Foundation;
                        jobToUpdate.Finishes = extractedData.Finishes ?? jobToUpdate.Finishes;
                        jobToUpdate.ElectricalSupplyNeeds =
                            extractedData.ElectricalSupplyNeeds
                            ?? jobToUpdate.ElectricalSupplyNeeds;
                        jobToUpdate.Stories =
                            extractedData.Stories > 0 ? extractedData.Stories : jobToUpdate.Stories;
                        jobToUpdate.BuildingSize =
                            extractedData.BuildingSize > 0
                                ? extractedData.BuildingSize
                                : jobToUpdate.BuildingSize;
                        await _context.SaveChangesAsync();
                    }
                }

                // Parse and save Trade Packages
                await ParseAndSaveTradePackages(jobId, aiResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract and update job details from AI response.");
            }
        }

        private async Task ParseAndSaveTradePackages(int jobId, string aiResponse)
        {
            try
            {
                var parsedTradePackages = new List<TradePackage>();

                var phaseBlocks = ExtractPhaseBlocks(aiResponse);
                foreach (var phaseBlock in phaseBlocks)
                {
                    var outputTwoTable = ExtractMarkdownTableAfterMarker(
                        phaseBlock,
                        "Output 2: Subcontractor Cost Breakdown",
                        "Trade"
                    );

                    if (outputTwoTable == null || !outputTwoTable.Rows.Any())
                    {
                        continue;
                    }

                    var outputOneTable = ExtractMarkdownTableAfterMarker(
                        phaseBlock,
                        "Output 1:",
                        "Item"
                    );

                    var materialByCsi = ParseMaterialTotalsByCsi(outputOneTable);
                    var tradeRows = ParseOutputTwoTradeRows(outputTwoTable);
                    if (!tradeRows.Any())
                    {
                        continue;
                    }

                    AllocateMaterialBudgets(tradeRows, materialByCsi);

                    foreach (var trade in tradeRows)
                    {
                        var laborBudget = Math.Max(0, Math.Round(trade.LaborBudget, 2));
                        var materialBudget = Math.Max(0, Math.Round(trade.MaterialBudget, 2));
                        var totalBudget = Math.Round(laborBudget + materialBudget, 2);

                        parsedTradePackages.Add(
                            new TradePackage
                            {
                                JobId = jobId,
                                TradeName = trade.TradeName,
                                ScopeOfWork = trade.ScopeOfWork,
                                EstimatedManHours = trade.EstimatedManHours,
                                HourlyRate = trade.HourlyRate,
                                LaborBudget = laborBudget,
                                MaterialBudget = materialBudget,
                                TotalBudget = totalBudget,
                                EffectiveBudget = totalBudget,
                                Budget = totalBudget,
                                CsiCode = trade.CsiCode,
                                Category = trade.Category,
                                LaborType = "Labor and Materials",
                                Status = "Draft",
                                PostedToMarketplace = false,
                                SourceType = "AI",
                                EstimatedDuration = "TBD",
                            }
                        );
                    }
                }

                if (parsedTradePackages.Any())
                {
                    var existingPackages = await _context
                        .TradePackages.Where(tp => tp.JobId == jobId)
                        .ToListAsync();

                    var existingByKey = existingPackages
                        .GroupBy(tp =>
                            BuildTradePackageMatchKey(tp.TradeName, tp.CsiCode, tp.Category)
                        )
                        .ToDictionary(g => g.Key, g => g.First());

                    var matchedKeys = new HashSet<string>();

                    foreach (var parsed in parsedTradePackages)
                    {
                        var key = BuildTradePackageMatchKey(
                            parsed.TradeName,
                            parsed.CsiCode,
                            parsed.Category
                        );

                        if (existingByKey.TryGetValue(key, out var existing))
                        {
                            matchedKeys.Add(key);
                            MergeParsedPackageIntoExisting(existing, parsed);
                            continue;
                        }

                        parsed.CreatedAt = DateTime.UtcNow;
                        _context.TradePackages.Add(parsed);
                    }

                    foreach (var existing in existingPackages)
                    {
                        var key = BuildTradePackageMatchKey(
                            existing.TradeName,
                            existing.CsiCode,
                            existing.Category
                        );

                        if (matchedKeys.Contains(key))
                        {
                            continue;
                        }

                        var isSystemLinked = string.Equals(
                            existing.SourceType,
                            "SYSTEM_LINKED",
                            StringComparison.OrdinalIgnoreCase
                        );

                        if ((existing.IsAutoGenerated || IsAiSource(existing)) && !isSystemLinked)
                        {
                            existing.IsInactive = true;
                            existing.IsHidden = true;
                            existing.PostedToMarketplace = false;
                            if (string.IsNullOrWhiteSpace(existing.Status))
                            {
                                existing.Status = "Draft";
                            }
                        }
                    }

                    await _context.SaveChangesAsync();
                    _logger.LogInformation(
                        $"Reconciled {parsedTradePackages.Count} parsed trade packages for Job {jobId}"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to parse Trade Packages for Job {jobId}");
            }
        }

        private sealed class ParsedTradeRow
        {
            public string TradeName { get; init; } = string.Empty;
            public string ScopeOfWork { get; init; } = string.Empty;
            public decimal EstimatedManHours { get; init; }
            public decimal HourlyRate { get; init; }
            public decimal LaborBudget { get; init; }
            public decimal MaterialBudget { get; set; }
            public string? CsiCode { get; init; }
            public string Category { get; init; } = "Trade";
        }

        private sealed class MarkdownTable
        {
            public List<string> Headers { get; init; } = new();
            public List<List<string>> Rows { get; init; } = new();
        }

        private static List<string> ExtractPhaseBlocks(string aiResponse)
        {
            var phaseRegex = new Regex(
                @"###\s*\*{0,2}Phase\s+\d+:[\s\S]*?(?=###\s*\*{0,2}Phase\s+\d+:|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );

            var blocks = phaseRegex.Matches(aiResponse).Select(m => m.Value).ToList();
            return blocks.Any() ? blocks : new List<string> { aiResponse };
        }

        private static MarkdownTable? ExtractMarkdownTableAfterMarker(
            string block,
            string marker,
            string requiredHeaderContains
        )
        {
            var markerIndex = block.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return null;
            }

            var tail = block.Substring(markerIndex);
            var lines = tail.Split('\n');

            var headerIndex = -1;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (!line.StartsWith("|"))
                {
                    continue;
                }

                if (line.Contains(requiredHeaderContains, StringComparison.OrdinalIgnoreCase))
                {
                    headerIndex = i;
                    break;
                }
            }

            if (headerIndex < 0)
            {
                return null;
            }

            var header = ParseMarkdownRow(lines[headerIndex]);
            var rows = new List<List<string>>();

            for (var i = headerIndex + 1; i < lines.Length; i++)
            {
                var current = lines[i].Trim();
                if (!current.StartsWith("|"))
                {
                    if (rows.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                if (current.Contains("---"))
                {
                    continue;
                }

                var parsed = ParseMarkdownRow(current);
                if (!parsed.Any())
                {
                    continue;
                }

                rows.Add(parsed);
            }

            return new MarkdownTable { Headers = header, Rows = rows };
        }

        private static List<string> ParseMarkdownRow(string row)
        {
            return row.Split('|')
                .Select(c => c.Trim().Replace("**", string.Empty))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();
        }

        private static Dictionary<string, decimal> ParseMaterialTotalsByCsi(MarkdownTable? table)
        {
            var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (table == null || !table.Rows.Any())
            {
                return totals;
            }

            var totalCostIdx = FindHeaderIndex(table.Headers, "Total Item Cost", "Total Cost");
            var csiIdx = FindHeaderIndex(table.Headers, "CSI MasterFormat Code", "CSI");
            var itemIdx = FindHeaderIndex(table.Headers, "Item");

            if (totalCostIdx < 0 || csiIdx < 0)
            {
                return totals;
            }

            foreach (var row in table.Rows)
            {
                var item = itemIdx >= 0 && itemIdx < row.Count ? row[itemIdx] : string.Empty;
                if (item.Contains("TOTAL", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var csi = csiIdx < row.Count ? NormalizeCsiCode(row[csiIdx]) : string.Empty;
                if (string.IsNullOrWhiteSpace(csi))
                {
                    continue;
                }

                var cost = totalCostIdx < row.Count ? ParseMoneyLikeValue(row[totalCostIdx]) : 0;
                if (cost <= 0)
                {
                    continue;
                }

                totals[csi] = totals.TryGetValue(csi, out var existing)
                    ? Math.Round(existing + cost, 2)
                    : Math.Round(cost, 2);
            }

            return totals;
        }

        private static List<ParsedTradeRow> ParseOutputTwoTradeRows(MarkdownTable table)
        {
            var rows = new List<ParsedTradeRow>();

            var tradeIdx = FindHeaderIndex(table.Headers, "Trade");
            var scopeIdx = FindHeaderIndex(table.Headers, "Scope of Work");
            var hoursIdx = FindHeaderIndex(table.Headers, "Estimated Man-Hours", "Man-Hours");
            var rateIdx = FindHeaderIndex(
                table.Headers,
                "Localized Hourly Rate",
                "Hourly Rate",
                "Rate"
            );
            var totalIdx = FindHeaderIndex(table.Headers, "Total Estimated Cost", "Total Cost");
            var csiIdx = FindHeaderIndex(table.Headers, "CSI MasterFormat Code", "CSI");

            foreach (var row in table.Rows)
            {
                if (tradeIdx < 0 || tradeIdx >= row.Count)
                {
                    continue;
                }

                var tradeName = row[tradeIdx].Trim();
                if (
                    string.IsNullOrWhiteSpace(tradeName)
                    || tradeName.Contains("TOTAL", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                var scope = scopeIdx >= 0 && scopeIdx < row.Count ? row[scopeIdx] : string.Empty;
                var manHours =
                    hoursIdx >= 0 && hoursIdx < row.Count ? ParseMoneyLikeValue(row[hoursIdx]) : 0;
                var hourlyRate =
                    rateIdx >= 0 && rateIdx < row.Count ? ParseMoneyLikeValue(row[rateIdx]) : 0;
                var totalCost =
                    totalIdx >= 0 && totalIdx < row.Count ? ParseMoneyLikeValue(row[totalIdx]) : 0;
                var csi = csiIdx >= 0 && csiIdx < row.Count ? NormalizeCsiCode(row[csiIdx]) : null;

                var laborBudget = totalCost > 0 ? totalCost : Math.Round(manHours * hourlyRate, 2);
                if (laborBudget <= 0)
                {
                    continue;
                }

                var category = "Trade";
                if (
                    tradeName.Contains("Supplier", StringComparison.OrdinalIgnoreCase)
                    || tradeName.Contains("Provider", StringComparison.OrdinalIgnoreCase)
                )
                {
                    category = "Supplier";
                }

                if (
                    tradeName.Contains("Rental", StringComparison.OrdinalIgnoreCase)
                    || tradeName.Contains("Equipment", StringComparison.OrdinalIgnoreCase)
                )
                {
                    category = "Equipment";
                }

                rows.Add(
                    new ParsedTradeRow
                    {
                        TradeName = tradeName,
                        ScopeOfWork = scope,
                        EstimatedManHours = Math.Round(manHours, 2),
                        HourlyRate = Math.Round(hourlyRate, 2),
                        LaborBudget = Math.Round(laborBudget, 2),
                        MaterialBudget = 0,
                        CsiCode = csi,
                        Category = category,
                    }
                );
            }

            return rows;
        }

        private static void AllocateMaterialBudgets(
            List<ParsedTradeRow> tradeRows,
            Dictionary<string, decimal> materialByCsi
        )
        {
            if (!tradeRows.Any() || !materialByCsi.Any())
            {
                return;
            }

            decimal unmatchedMaterial = 0;

            foreach (var materialEntry in materialByCsi)
            {
                var csi = materialEntry.Key;
                var materialTotal = materialEntry.Value;
                if (materialTotal <= 0)
                {
                    continue;
                }

                var matchingTrades = tradeRows
                    .Where(t => NormalizeCsiCode(t.CsiCode) == csi)
                    .ToList();

                if (!matchingTrades.Any())
                {
                    unmatchedMaterial += materialTotal;
                    continue;
                }

                var laborSum = matchingTrades.Sum(t => Math.Max(0, t.LaborBudget));
                if (laborSum <= 0)
                {
                    var evenShare = Math.Round(materialTotal / matchingTrades.Count, 2);
                    foreach (var trade in matchingTrades)
                    {
                        trade.MaterialBudget += evenShare;
                    }

                    continue;
                }

                decimal allocated = 0;
                for (var i = 0; i < matchingTrades.Count; i++)
                {
                    var trade = matchingTrades[i];
                    if (i == matchingTrades.Count - 1)
                    {
                        trade.MaterialBudget += Math.Round(materialTotal - allocated, 2);
                        continue;
                    }

                    var share = Math.Round(materialTotal * (trade.LaborBudget / laborSum), 2);
                    trade.MaterialBudget += share;
                    allocated += share;
                }
            }

            if (unmatchedMaterial <= 0)
            {
                return;
            }

            var fallbackTrades = tradeRows
                .Where(t => string.Equals(t.Category, "Trade", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!fallbackTrades.Any())
            {
                fallbackTrades = tradeRows;
            }

            if (!fallbackTrades.Any())
            {
                return;
            }

            var fallbackLabor = fallbackTrades.Sum(t => Math.Max(0, t.LaborBudget));
            if (fallbackLabor <= 0)
            {
                var evenShare = Math.Round(unmatchedMaterial / fallbackTrades.Count, 2);
                foreach (var trade in fallbackTrades)
                {
                    trade.MaterialBudget += evenShare;
                }

                return;
            }

            decimal distributed = 0;
            for (var i = 0; i < fallbackTrades.Count; i++)
            {
                var trade = fallbackTrades[i];
                if (i == fallbackTrades.Count - 1)
                {
                    trade.MaterialBudget += Math.Round(unmatchedMaterial - distributed, 2);
                    continue;
                }

                var share = Math.Round(unmatchedMaterial * (trade.LaborBudget / fallbackLabor), 2);
                trade.MaterialBudget += share;
                distributed += share;
            }
        }

        private static int FindHeaderIndex(List<string> headers, params string[] keys)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var header = headers[i];
                if (keys.Any(k => header.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string NormalizeCsiCode(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var cleaned = Regex.Replace(raw, @"\s+", " ").Trim();
            return cleaned.ToUpperInvariant();
        }

        private static void MergeParsedPackageIntoExisting(
            TradePackage existing,
            TradePackage parsed
        )
        {
            existing.TradeName = parsed.TradeName;
            existing.ScopeOfWork = parsed.ScopeOfWork;
            existing.EstimatedManHours = parsed.EstimatedManHours;
            existing.HourlyRate = parsed.HourlyRate;
            existing.CsiCode = parsed.CsiCode;
            existing.Category = parsed.Category;
            existing.TotalBudget = parsed.TotalBudget;
            existing.LaborBudget = parsed.LaborBudget;
            existing.MaterialBudget = parsed.MaterialBudget;
            existing.EstimatedDuration = parsed.EstimatedDuration;

            if (string.IsNullOrWhiteSpace(existing.SourceType))
            {
                existing.SourceType = "AI";
            }

            if (string.IsNullOrWhiteSpace(existing.LaborType))
            {
                existing.LaborType = "Labor and Materials";
            }

            var isLaborOnly = IsLaborOnly(existing.LaborType);
            existing.EffectiveBudget = isLaborOnly ? existing.LaborBudget : existing.TotalBudget;
            existing.Budget = existing.EffectiveBudget;

            if (isLaborOnly)
            {
                existing.IsHidden = false;
                existing.IsInactive = false;
            }
        }

        private static string BuildTradePackageMatchKey(
            string? tradeName,
            string? csiCode,
            string? category
        )
        {
            var normalizedTrade = (tradeName ?? string.Empty).Trim().ToLowerInvariant();
            var normalizedCsi = (csiCode ?? string.Empty).Trim().ToLowerInvariant();
            var normalizedCategory = (category ?? "Trade").Trim().ToLowerInvariant();
            return $"{normalizedTrade}|{normalizedCsi}|{normalizedCategory}";
        }

        private static bool IsAiSource(TradePackage tradePackage)
        {
            return string.Equals(tradePackage.SourceType, "AI", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLaborOnly(string? laborType)
        {
            var normalized = (laborType ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "labor" || normalized == "labor only";
        }

        private static decimal ParseMoneyLikeValue(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return 0;
            }
          
            var cleaned = Regex.Replace(raw, @"[^0-9\.-]", "");
            cleaned = cleaned.Replace(".", ",");
            if (decimal.TryParse(cleaned, out var parsed))
            {
                return parsed;
            }

            return 0;
        }

        private async Task<string> ExecuteSequentialRenovationPromptsAsync(
            string conversationId,
            string userId,
            string initialResponse,
            int jobId,
            string? connectionId,
            DateTime analysisStartedAtUtc
        )
        {
            var stringBuilder = new StringBuilder();
            initialResponse = EnsurePhaseHeading(initialResponse, 1, InitialAnalysisPhaseTitle);
            stringBuilder.Append(initialResponse);

            var promptNames = new[]
            {
                "renovation-01-demolition",
                "renovation-02-structural-alterations",
                "renovation-03-rough-in-mep",
                "renovation-04-insulation-drywall",
                "renovation-05-interior-finishes",
                "renovation-06-fixtures-fittings-equipment",
                "renovation-07-cost-breakdown-summary",
                "renovation-08-project-timeline",
                "renovation-09-environmental-impact",
                "renovation-10-final-review-rebuttal",
            };

            try
            {
                string lastResponse;
                var completedPrompts = await GetCompletedPromptNames(jobId);
                var savedModelPrompts = await GetSavedModelPromptNames(jobId);

                foreach (var promptName in savedModelPrompts)
                {
                    if (!completedPrompts.Contains(promptName))
                    {
                        await MarkPromptCompleted(jobId, promptName);
                        completedPrompts.Add(promptName);
                    }
                }

                int completedCount = promptNames.Count(p => completedPrompts.Contains(p));
                foreach (var promptName in promptNames)
                {
                    if (
                        completedPrompts.Contains(promptName)
                        && !IsScopeSnapshotPromptKey(promptName)
                    )
                    {
                        continue;
                    }

                    var step = completedCount + 1;
                    _logger.LogInformation(
                        "Executing step {Step} of {TotalSteps}: {PromptName} for conversation {ConversationId}",
                        step,
                        promptNames.Length,
                        promptName,
                        conversationId
                    );

                    await TrySendAnalysisProgressUpdate(
                        connectionId,
                        new Middleware.AnalysisProgressUpdate
                        {
                            JobId = jobId,
                            StatusMessage = $"Analyzing: {FormatPromptStatusLabel(promptName)}",
                            CurrentStep = step,
                            TotalSteps = promptNames.Length,
                            IsComplete = false,
                            HasFailed = false,
                        }
                    );

                    await UpdateAnalysisState(
                        jobId,
                        $"Analyzing: {FormatPromptStatusLabel(promptName)}",
                        step,
                        promptNames.Length
                    );

                    var promptText = await _promptManager.GetPromptAsync(
                        "RenovationPrompts/",
                        $"{promptName}.txt"
                    );
                    promptText = await ApplyDeterministicScopePromptInstructionsAsync(
                        jobId,
                        promptName,
                        promptText
                    );
                    var phaseTitle = FormatPromptStatusLabel(promptName);
                    var phasePrefix = BuildPhaseInstructionPrefix(step, phaseTitle);
                    (lastResponse, _) = await _aiService.ContinueConversationAsync(
                        conversationId,
                        userId,
                        $"{phasePrefix}\n\n{promptText}",
                        null,
                        true
                    );
                    lastResponse = EnsurePhaseHeading(lastResponse, step, phaseTitle);

                    await _conversationRepo.AddMessageIfNotExistsAsync(
                        new Message
                        {
                            ConversationId = conversationId,
                            Role = "model",
                            Content = lastResponse,
                        }
                    );

                    await MarkModelPromptSaved(jobId, promptName);

                    stringBuilder.Append("\n\n---\n\n");
                    stringBuilder.Append(lastResponse);

                    var persisted = await ParseAndBroadcastPromptResult(
                        jobId,
                        promptName,
                        lastResponse,
                        connectionId
                    );

                    if (persisted)
                    {
                        var normalizedPromptKey = NormalizePromptKey(promptName);
                        var exists = await _context.JobPromptResults.AsNoTracking()
                            .AnyAsync(r => r.JobId == jobId && r.PromptKey == normalizedPromptKey);
                        if (!exists)
                        {
                            _logger.LogError(
                                "Persist pipeline returned persisted=true but no JobPromptResults row exists. jobId={JobId} promptKey={PromptKey}",
                                jobId,
                                normalizedPromptKey
                            );
                            persisted = false;
                        }
                    }

                    if (persisted)
                    {
                        await MarkPromptCompleted(jobId, promptName);
                        completedCount++;
                    }
                }

                await EnsureScopeSnapshotCompletenessAsync(jobId, analysisStartedAtUtc);

                await TrySendAnalysisProgressUpdate(
                    connectionId,
                    new Middleware.AnalysisProgressUpdate
                    {
                        JobId = jobId,
                        StatusMessage = "Analysis complete.",
                        CurrentStep = promptNames.Length,
                        TotalSteps = promptNames.Length,
                        IsComplete = true,
                        HasFailed = false,
                    }
                );

                _logger.LogInformation(
                    "Full sequential renovation analysis completed successfully for conversation {ConversationId}",
                    conversationId
                );
                return await BuildFullReportFromConversation(conversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An error occurred during sequential renovation prompt execution for conversation {ConversationId}",
                    conversationId
                );
                await TrySendAnalysisProgressUpdate(
                    connectionId,
                    new Middleware.AnalysisProgressUpdate
                    {
                        JobId = jobId,
                        StatusMessage = "Analysis failed during sequential execution.",
                        IsComplete = false,
                        HasFailed = true,
                        ErrorMessage = ex.Message,
                    }
                );
                throw;
            }
        }

        public async Task RefreshTradePackagesAsync(int jobId)
        {
            var processingResult = await _context
                .DocumentProcessingResults.AsNoTracking()
                .Where(r => r.JobId == jobId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (processingResult != null && !string.IsNullOrEmpty(processingResult.FullResponse))
            {
                _logger.LogInformation($"Refreshing trade packages for Job {jobId}");
                await ParseAndSaveTradePackages(jobId, processingResult.FullResponse);
            }
            else
            {
                _logger.LogWarning(
                    $"No processing result found for Job {jobId} to refresh trade packages."
                );
            }
        }

        public async Task<string> AnalyzeBidsAsync(
            List<BidModel> bids,
            string comparisonType,
            TradePackage? tradePackage = null
        )
        {
            if (bids == null || bids.Count == 0)
            {
                return JsonSerializer.Serialize(
                    new
                    {
                        summary = "No bids were available for analysis.",
                        recommendedBidId = (int?)null,
                        reasons = new[] { "No bids submitted for this package." },
                        topCandidates = Array.Empty<object>(),
                    }
                );
            }

            var submittedBids = bids.Where(b =>
                    !string.Equals(b.Status, "Withdrawn", StringComparison.OrdinalIgnoreCase)
                )
                .ToList();

            if (!submittedBids.Any())
            {
                return JsonSerializer.Serialize(
                    new
                    {
                        summary = "No active bids were available for analysis.",
                        recommendedBidId = (int?)null,
                        reasons = new[] { "All bids are currently withdrawn." },
                        topCandidates = Array.Empty<object>(),
                    }
                );
            }

            var lowestAmount = submittedBids.Min(b => b.Amount <= 0 ? decimal.MaxValue : b.Amount);
            if (lowestAmount == decimal.MaxValue)
            {
                lowestAmount = submittedBids.Min(b => b.Amount);
            }

            var lane = string.IsNullOrWhiteSpace(comparisonType) ? "Trade" : comparisonType;
            var promptPayload = BuildBidComparisonPayload(
                submittedBids,
                lane,
                tradePackage,
                lowestAmount
            );

            try
            {
                var systemPersonaPrompt = await _promptManager.GetPromptAsync(
                    null,
                    BidComparisonPromptKey
                );

                var (analysisResult, _) = await _aiService.StartTextConversationAsync(
                    "system-user",
                    systemPersonaPrompt,
                    promptPayload
                );

                if (!string.IsNullOrWhiteSpace(analysisResult))
                {
                    var parsedFromAi = TryParseBidComparisonJson(analysisResult, submittedBids);
                    if (parsedFromAi != null)
                    {
                        return JsonSerializer.Serialize(parsedFromAi);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "AI bid comparison failed; falling back to deterministic scoring for lane {Lane}.",
                    lane
                );
            }

            var scored = submittedBids
                .Select(bid =>
                {
                    var buildigRating = (double)(bid.User?.ProbuildRating ?? 0);
                    var googleRating = (double)(bid.User?.GoogleRating ?? 0);
                    var blendedRating = Math.Max(
                        0,
                        Math.Min(5, (buildigRating * 0.6) + (googleRating * 0.4))
                    );

                    var amount = bid.Amount <= 0 ? 1 : bid.Amount;
                    var priceScore = lowestAmount <= 0 ? 0 : (double)(lowestAmount / amount);
                    var normalizedPrice = Math.Max(0, Math.Min(1, priceScore));
                    var normalizedRating = blendedRating / 5.0;

                    var score = (normalizedPrice * 0.7) + (normalizedRating * 0.3);
                    return new
                    {
                        Bid = bid,
                        Score = Math.Round(score, 4),
                        BlendedRating = Math.Round(blendedRating, 2),
                    };
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Bid.Amount)
                .ToList();

            var recommended = scored.First();
            var recommendedAmount = recommended.Bid.Amount;
            var pctVsLowest =
                lowestAmount > 0
                    ? Math.Round(((recommendedAmount - lowestAmount) / lowestAmount) * 100m, 1)
                    : 0;

            var reasons = new List<string>
            {
                $"Best combined score for {lane} selection using cost competitiveness and rating.",
                $"Quoted amount: {recommendedAmount:C0}; market low: {lowestAmount:C0} ({pctVsLowest:+0.0;-0.0;0.0}% vs low).",
                $"Blended contractor rating: {recommended.BlendedRating:0.0}/5.",
            };

            var topCandidates = scored
                .Take(3)
                .Select(x =>
                {
                    var delta =
                        lowestAmount > 0
                            ? Math.Round(((x.Bid.Amount - lowestAmount) / lowestAmount) * 100m, 1)
                            : 0;

                    return new
                    {
                        bidId = x.Bid.Id,
                        score = x.Score,
                        amount = x.Bid.Amount,
                        rating = x.BlendedRating,
                        reason = $"{x.Bid.Amount:C0} ({delta:+0.0;-0.0;0.0}% vs low), rating {x.BlendedRating:0.0}/5, status {x.Bid.Status}.",
                    };
                })
                .ToList();

            var response = new
            {
                summary = $"Recommended bid #{recommended.Bid.Id} based on weighted price and rating for {lane.ToLowerInvariant()} comparison.",
                recommendedBidId = recommended.Bid.Id,
                reasons,
                topCandidates,
            };

            return JsonSerializer.Serialize(response);
        }

        private string BuildBidComparisonPayload(
            List<BidModel> bids,
            string comparisonType,
            TradePackage? tradePackage,
            decimal lowestAmount
        )
        {
            var payload = new
            {
                comparisonType,
                tradePackage = tradePackage == null
                    ? null
                    : new
                    {
                        tradePackage.Id,
                        tradePackage.TradeName,
                        tradePackage.Category,
                        tradePackage.ScopeOfWork,
                        tradePackage.CsiCode,
                        tradePackage.LaborType,
                        tradePackage.Budget,
                        tradePackage.LaborBudget,
                        tradePackage.MaterialBudget,
                    },
                bids = bids.Select(b => new
                {
                    bidId = b.Id,
                    amount = b.Amount,
                    status = b.Status,
                    bidderName = b.User?.CompanyName
                        ?? string.Join(
                            " ",
                            new[] { b.User?.FirstName, b.User?.LastName }.Where(x =>
                                !string.IsNullOrWhiteSpace(x)
                            )
                        )
                        ?? b.User?.UserName
                        ?? $"Bidder #{b.Id}",
                    ratings = new
                    {
                        buildig = b.User?.ProbuildRating ?? 0,
                        google = b.User?.GoogleRating ?? 0,
                    },
                    deltaVsLowestPct = lowestAmount > 0
                        ? Math.Round(((b.Amount - lowestAmount) / lowestAmount) * 100m, 1)
                        : 0,
                }),
                expectedOutput = new
                {
                    summary = "string",
                    recommendedBidId = "number",
                    reasons = new[] { "string" },
                    topCandidates = new[]
                    {
                        new
                        {
                            bidId = 0,
                            score = 0.0,
                            reason = "string",
                        },
                    },
                },
                instructions = "Return valid JSON only. Do not include markdown. Choose recommendedBidId from supplied bidId values.",
            };

            return JsonSerializer.Serialize(payload);
        }

        private object? TryParseBidComparisonJson(string aiResponse, List<BidModel> bids)
        {
            try
            {
                var start = aiResponse.IndexOf('{');
                var end = aiResponse.LastIndexOf('}');
                if (start < 0 || end <= start)
                {
                    return null;
                }

                var json = aiResponse.Substring(start, end - start + 1);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("recommendedBidId", out var recommendedEl))
                {
                    return null;
                }

                var recommendedId =
                    recommendedEl.ValueKind == JsonValueKind.Number ? recommendedEl.GetInt32()
                    : int.TryParse(recommendedEl.GetString(), out var parsed) ? parsed
                    : 0;

                if (!bids.Any(b => b.Id == recommendedId))
                {
                    return null;
                }

                var summary = root.TryGetProperty("summary", out var summaryEl)
                    ? (summaryEl.GetString() ?? "Analysis complete.")
                    : "Analysis complete.";

                var reasons = new List<string>();
                if (
                    root.TryGetProperty("reasons", out var reasonsEl)
                    && reasonsEl.ValueKind == JsonValueKind.Array
                )
                {
                    foreach (var reason in reasonsEl.EnumerateArray())
                    {
                        if (reason.ValueKind == JsonValueKind.String)
                        {
                            var text = reason.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                reasons.Add(text);
                            }
                        }
                    }
                }

                var topCandidates = new List<object>();
                if (
                    root.TryGetProperty("topCandidates", out var candidatesEl)
                    && candidatesEl.ValueKind == JsonValueKind.Array
                )
                {
                    foreach (var candidate in candidatesEl.EnumerateArray())
                    {
                        if (candidate.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var bidId =
                            candidate.TryGetProperty("bidId", out var bidIdEl)
                            && bidIdEl.ValueKind == JsonValueKind.Number
                                ? bidIdEl.GetInt32()
                                : 0;

                        if (!bids.Any(b => b.Id == bidId))
                        {
                            continue;
                        }

                        var score =
                            candidate.TryGetProperty("score", out var scoreEl)
                            && scoreEl.ValueKind == JsonValueKind.Number
                                ? scoreEl.GetDouble()
                                : 0d;

                        var reason = candidate.TryGetProperty("reason", out var reasonEl)
                            ? (reasonEl.GetString() ?? string.Empty)
                            : string.Empty;

                        topCandidates.Add(
                            new
                            {
                                bidId,
                                score,
                                reason,
                            }
                        );
                    }
                }

                if (!topCandidates.Any())
                {
                    topCandidates = bids.Take(3)
                        .Select(b =>
                            (object)
                                new
                                {
                                    bidId = b.Id,
                                    score = 0d,
                                    reason = "Candidate",
                                }
                        )
                        .ToList();
                }

                return new
                {
                    summary,
                    recommendedBidId = recommendedId,
                    reasons = reasons.Any()
                        ? reasons.ToArray()
                        : ["AI selected the strongest overall quote."],
                    topCandidates,
                };
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> GenerateFeedbackForUnsuccessfulBidderAsync(
            BidModel bid,
            BidModel winningBid
        )
        {
            //var user = bid.User;
            //if (user != null)
            //{
            //    bool isFreeTier =
            //        user.SubscriptionPackage == "Basic (Free) ($0.00)"
            //        || string.IsNullOrEmpty(user.SubscriptionPackage)
            //        || user.SubscriptionPackage.ToUpper() == "BASIC";

            //    if (isFreeTier)
            //    {
            //        return "Feedback reports are only available for users on a paid subscription tier.";
            //    }
            //}

            //var prompt = await _promptManager.GetPromptAsync(
            //    "ComparisonPrompts/",
            //    "unsuccessful-bid-prompt.txt"
            //);

            //var unsuccessfulQuote = await _context.Quotes.FirstOrDefaultAsync(q =>
            //    q.Id == bid.QuoteId
            //);
            //var winningQuote = await _context.Quotes.FirstOrDefaultAsync(q =>
            //    q.Id == winningBid.QuoteId
            //);

            //var unsuccessfulBidAnalysis = new
            //{
            //    bid.Id,
            //    bid.Amount,
            //    QuoteDetails = unsuccessfulQuote,
            //};

            //var winningBidBenchmark = new { winningBid.Amount, QuoteDetails = winningQuote };

            //var promptInput = new
            //{
            //    ProjectName = bid.Job?.ProjectName,
            //    WorkPackage = bid.Job?.JobType,
            //    OurCompanyName = "Buildig", // Or fetch dynamically
            //    UnsuccessfulSubcontractorName = user?.UserName,
            //    AnalysisOfUnsuccessfulQuotation = JsonSerializer.Serialize(unsuccessfulBidAnalysis),
            //    WinningBidBenchmark = JsonSerializer.Serialize(winningBidBenchmark),
            //};

            //var fullPrompt = prompt
            //    .Replace(
            //        "[e.g., The Falcon Heights Residential Development]",
            //        promptInput.ProjectName
            //    )
            //    .Replace(
            //        "[e.g., Structural Steel Fabrication and Erection]",
            //        promptInput.WorkPackage
            //    )
            //    .Replace("[Your Company Name]", promptInput.OurCompanyName)
            //    .Replace(
            //        "[Enter the name of the company you are writing to]",
            //        promptInput.UnsuccessfulSubcontractorName
            //    )
            //    .Replace(
            //        "[Paste the detailed analysis of the subcontractor's quote here, including price, schedule, inclusions, exclusions, compliance notes.]",
            //        promptInput.AnalysisOfUnsuccessfulQuotation
            //    )
            //    .Replace(
            //        "[Summarize the key advantages of the winning bid. For example: \"Final price was R 1,150,000 (8% lower). Proposed schedule was 16 weeks (2 weeks shorter). Fully compliant with specifications. Included a detailed plan for managing material price volatility.\"]",
            //        promptInput.WinningBidBenchmark
            //    );

            //var (analysisResult, _) = await _aiService.StartMultimodalConversationAsync(
            //    "system-user",
            //    null,
            //    fullPrompt,
            //    "Generate a feedback report for the provided bid."
            //);

            //return analysisResult;
            return null;
        }

        private string ExtractBlueprintJson(string aiResponse)
        {
            var jsonRegex = new Regex(@"```json\s*(\{.*?\})\s*```", RegexOptions.Singleline);
            var match = jsonRegex.Match(aiResponse);
            if (match.Success)
            {
                _logger.LogInformation("Successfully extracted blueprint JSON from AI response.");
                return match.Groups[1].Value;
            }

            _logger.LogError("Failed to find or extract blueprint JSON from the AI response.");
            throw new InvalidOperationException("Could not parse blueprint JSON from AI response.");
        }

        public async Task<PlanningDataDto> GetPlanningDataAsync(int jobId)
        {
            var result = await _context
                .DocumentProcessingResults.Where(r => r.JobId == jobId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            var dto = new PlanningDataDto();

            if (result == null || string.IsNullOrEmpty(result.FullResponse))
            {
                return dto;
            }

            string fullResponse = result.FullResponse;

            // Parse Procurement Schedule
            // Looking for table under "### Phase 24: Procurement & Submittal Schedule" or similar
            var procurementRegex = new Regex(
                @"### Phase \d+: Procurement & Submittal Schedule.*?\| Item \| CSI MasterFormat Code \|.*?(\n\n|###|$)",
                RegexOptions.Singleline
            );
            var procurementMatch = procurementRegex.Match(fullResponse);

            if (procurementMatch.Success)
            {
                var lines = procurementMatch.Value.Split('\n');
                foreach (var line in lines)
                {
                    if (
                        string.IsNullOrWhiteSpace(line)
                        || !line.Trim().StartsWith("|")
                        || line.Contains("Item")
                        || line.Contains("---")
                    )
                        continue;

                    var cols = line.Split('|').Select(c => c.Trim()).ToList();
                    // | Item | CSI | Vendor | Lead Time | Need-By | Delivery | Order | Approval | Submittal |
                    // 0: Item
                    // 2: Vendor
                    // 3: Lead Time

                    if (cols.Count >= 4)
                    {
                        dto.ProcurementItems.Add(
                            new ProcurementItemDto
                            {
                                Item = cols[0],
                                Vendor = cols[2],
                                LeadTime = cols[3] + " weeks",
                                Status = "Not Ordered",
                                EstimatedCost = 0, // Cost not in this table usually
                            }
                        );
                    }
                }
            }

            // Parse Critical Path / Timeline
            // Looking for table under "### Phase 22: Timeline"
            var timelineRegex = new Regex(
                @"### Phase \d+: Timeline.*?\| Phase \| Task \|.*?(\n\n|###|$)",
                RegexOptions.Singleline
            );
            var timelineMatch = timelineRegex.Match(fullResponse);

            if (timelineMatch.Success)
            {
                var lines = timelineMatch.Value.Split('\n');
                int idCounter = 1;
                foreach (var line in lines)
                {
                    if (
                        string.IsNullOrWhiteSpace(line)
                        || !line.Trim().StartsWith("|")
                        || line.Contains("Phase")
                        || line.Contains("---")
                    )
                        continue;

                    var cols = line.Split('|')
                        .Select(c => c.Trim())
                        .Where(c => !string.IsNullOrEmpty(c))
                        .ToList();
                    // | Phase | Task | Duration | Crew | Start | End | Dependencies | CSI |
                    // 0: Phase
                    // 1: Task
                    // 2: Duration

                    if (cols.Count >= 6)
                    {
                        var phaseName = cols[0];
                        // If phase is empty (merged cell behavior), use previous
                        if (string.IsNullOrEmpty(phaseName) && dto.CriticalPath.Any())
                        {
                            phaseName = dto.CriticalPath.Last().Phase;
                        }

                        dto.CriticalPath.Add(
                            new CriticalPathPhaseDto
                            {
                                Id = $"task-{idCounter++}",
                                Phase = phaseName,
                                Materials = cols[1], // Mapping Task to Materials/Desc
                                Duration = cols[2] + " days",
                                StartDay = 0, // Need to calc from date or leave 0
                                EndDay = 0,
                                Dependencies = cols.Count > 6 ? cols[6] : "",
                            }
                        );
                    }
                }
            }
            return dto;
        }

        private static string FormatPromptStatusLabel(string? promptKeyOrName)
        {
            var normalized = (promptKeyOrName ?? string.Empty).Replace(
                ".txt",
                string.Empty,
                StringComparison.OrdinalIgnoreCase
            );

            normalized = Regex.Replace(
                normalized,
                @"^(?:prompt|renovation)-\d{1,2}-",
                string.Empty,
                RegexOptions.IgnoreCase
            );

            normalized = normalized.Replace("-", " ").Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "Analysis";
            }

            return Regex.Replace(
                normalized.ToLowerInvariant(),
                @"\b[a-z]",
                m => m.Value.ToUpperInvariant()
            );
        }

        private async Task<string> ApplyDeterministicScopePromptInstructionsAsync(
            int jobId,
            string? promptKeyOrName,
            string promptText
        )
        {
            if (string.IsNullOrWhiteSpace(promptText))
            {
                return promptText;
            }

            var schema = GetExpectedScopeSchemaVersion(promptKeyOrName);
            if (string.IsNullOrWhiteSpace(schema))
            {
                // Even when no strict JSON schema contract is required, keep cross-phase anchors
                // stable between Scope Review and Estimating prompts.
                var valueLockOnly = await BuildCrossPhaseValueLockContractAsync(
                    jobId,
                    promptKeyOrName
                );
                if (string.IsNullOrWhiteSpace(valueLockOnly))
                {
                    return promptText;
                }
                return $"{valueLockOnly}\n\n{promptText}";
            }

            var contract = $@"OUTPUT CONTRACT (MANDATORY):
- Return ONLY one JSON object in a single fenced code block: ```json ... ```.
- Do not return markdown tables, bullets, prose, or explanations outside that JSON block.
- The root `schemaVersion` MUST be exactly ""{schema}"".
- Keep the exact field layout defined by this prompt; do not rename keys.
- Use JSON null (not NULL) when a value is unknown.
- For money/time fields: return numeric values only (no currency symbols or units in numeric fields).
- If an input is missing, keep the field present and set it to null or an empty array/object as appropriate.
- All returned JSON must be parseable by a strict JSON parser.";

            var valueLockContract = await BuildCrossPhaseValueLockContractAsync(jobId, promptKeyOrName);
            if (string.IsNullOrWhiteSpace(valueLockContract))
            {
                return $"{contract}\n\n{promptText}";
            }

            return $"{contract}\n\n{valueLockContract}\n\n{promptText}";
        }

        private async Task<string?> BuildCrossPhaseValueLockContractAsync(
            int jobId,
            string? promptKeyOrName
        )
        {
            var key = NormalizePromptKey(promptKeyOrName);
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            // Keep prompt-25 and executive summary locked as well; other scope snapshot prompts remain isolated.
            var isPrompt25 = string.Equals(
                key,
                "prompt-25-cost-breakdowns",
                StringComparison.OrdinalIgnoreCase
            );
            var isExecutiveSummary = string.Equals(
                key,
                "executive-summary-prompt",
                StringComparison.OrdinalIgnoreCase
            );
            if (string.Equals(key, "prompt-00-initial-analysis", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (IsScopeSnapshotPromptKey(key) && !isPrompt25 && !isExecutiveSummary)
            {
                return null;
            }

            if (!Regex.IsMatch(key, @"^prompt-\d{2}-", RegexOptions.IgnoreCase))
            {
                return null;
            }

            var timelineRow = await _context
                .JobPromptResults.AsNoTracking()
                .Where(
                    r =>
                        r.JobId == jobId
                        && (
                            r.PromptKey == "prompt-21-timeline"
                            || r.PromptKey == "prompt-21-timeline.txt"
                        )
                        && r.ParsedJson != null
                )
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => r.ParsedJson)
                .FirstOrDefaultAsync();

            var costRow = await _context
                .JobPromptResults.AsNoTracking()
                .Where(
                    r =>
                        r.JobId == jobId
                        && (
                            r.PromptKey == "prompt-25-cost-breakdowns"
                            || r.PromptKey == "prompt-25-cost-breakdowns.txt"
                        )
                        && r.ParsedJson != null
                )
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => r.ParsedJson)
                .FirstOrDefaultAsync();

            var initialRow = await _context
                .JobPromptResults.AsNoTracking()
                .Where(
                    r =>
                        r.JobId == jobId
                        && (
                            r.PromptKey == "prompt-00-initial-analysis"
                            || r.PromptKey == "prompt-00-initial-analysis.txt"
                        )
                        && r.ParsedJson != null
                )
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => r.ParsedJson)
                .FirstOrDefaultAsync();

            string? currencyCode = null;
            string? unitsSystem = null;
            string? projectStartDate = null;
            double? grandTotalBidPrice = null;
            double? directInsurableSubtotal = null;
            double? conditionedAreaValue = null;

            if (!string.IsNullOrWhiteSpace(timelineRow))
            {
                try
                {
                    using var tDoc = JsonDocument.Parse(timelineRow);
                    if (TryGetPropertyCaseInsensitive(tDoc.RootElement, "currencyCode", out var c))
                    {
                        currencyCode = c.GetString();
                    }
                    if (TryGetPropertyCaseInsensitive(tDoc.RootElement, "unitsSystem", out var u))
                    {
                        unitsSystem = u.GetString();
                    }
                    if (
                        TryGetPropertyCaseInsensitive(tDoc.RootElement, "data", out var d)
                        && d.ValueKind == JsonValueKind.Object
                        && TryGetPropertyCaseInsensitive(d, "projectStartDate", out var psd)
                    )
                    {
                        projectStartDate = psd.ValueKind == JsonValueKind.String ? psd.GetString() : null;
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(costRow))
            {
                try
                {
                    using var cDoc = JsonDocument.Parse(costRow);
                    if (
                        string.IsNullOrWhiteSpace(currencyCode)
                        && TryGetPropertyCaseInsensitive(cDoc.RootElement, "currencyCode", out var cc)
                    )
                    {
                        currencyCode = cc.GetString();
                    }
                    if (
                        string.IsNullOrWhiteSpace(unitsSystem)
                        && TryGetPropertyCaseInsensitive(cDoc.RootElement, "unitsSystem", out var us)
                    )
                    {
                        unitsSystem = us.GetString();
                    }
                    if (
                        TryGetPropertyCaseInsensitive(cDoc.RootElement, "data", out var d)
                        && d.ValueKind == JsonValueKind.Object
                    )
                    {
                        if (TryGetFiniteNumber(d, "grandTotalBidPrice", out var gt))
                        {
                            grandTotalBidPrice = gt;
                        }
                        if (TryGetFiniteNumber(d, "directInsurableSubtotal", out var ds))
                        {
                            directInsurableSubtotal = ds;
                        }
                        if (
                            TryGetPropertyCaseInsensitive(d, "conditionedArea", out var ca)
                            && ca.ValueKind == JsonValueKind.Object
                            && TryGetFiniteNumber(ca, "value", out var cv)
                        )
                        {
                            conditionedAreaValue = cv;
                        }
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(initialRow))
            {
                try
                {
                    using var iDoc = JsonDocument.Parse(initialRow);
                    if (
                        string.IsNullOrWhiteSpace(currencyCode)
                        && TryGetPropertyCaseInsensitive(iDoc.RootElement, "currencyCode", out var cc)
                    )
                    {
                        currencyCode = cc.GetString();
                    }
                    if (
                        string.IsNullOrWhiteSpace(unitsSystem)
                        && TryGetPropertyCaseInsensitive(iDoc.RootElement, "unitsSystem", out var us)
                    )
                    {
                        unitsSystem = us.GetString();
                    }
                    if (
                        !conditionedAreaValue.HasValue
                        && TryGetFiniteNumber(iDoc.RootElement, "buildingSize", out var bs)
                    )
                    {
                        conditionedAreaValue = bs;
                    }
                }
                catch { }
            }

            var anchors = new List<string>();
            if (!string.IsNullOrWhiteSpace(currencyCode))
                anchors.Add($@"- currencyCode = ""{currencyCode}""");
            if (!string.IsNullOrWhiteSpace(unitsSystem))
                anchors.Add($@"- unitsSystem = ""{unitsSystem}""");
            if (!string.IsNullOrWhiteSpace(projectStartDate))
                anchors.Add($@"- projectStartDate = ""{projectStartDate}""");
            if (grandTotalBidPrice.HasValue)
                anchors.Add($"- grandTotalBidPrice = {grandTotalBidPrice.Value:F2}");
            if (directInsurableSubtotal.HasValue)
                anchors.Add($"- directInsurableSubtotal = {directInsurableSubtotal.Value:F2}");
            if (conditionedAreaValue.HasValue)
                anchors.Add($"- conditionedArea.value = {conditionedAreaValue.Value:F2}");

            if (isPrompt25)
            {
                var phaseDirectTotals = await GetPrompt04To19DirectTotalsAsync(jobId);
                if (phaseDirectTotals.hasTotals)
                {
                    anchors.Add($"- prompt04to19.totalMaterialCost = {phaseDirectTotals.materialTotal:F2}");
                    anchors.Add($"- prompt04to19.totalLaborCost = {phaseDirectTotals.laborTotal:F2}");
                    anchors.Add($"- prompt04to19.directSubtotal = {phaseDirectTotals.directTotal:F2}");
                }
            }

            if (anchors.Count == 0)
            {
                return null;
            }

            var prompt25HardLock = isPrompt25
                ? @"
- For prompt-25-cost-breakdowns specifically:
  - `data.totalMaterialCost` MUST equal `prompt04to19.totalMaterialCost`.
  - `data.totalLaborCost` MUST equal `prompt04to19.totalLaborCost`.
  - `data.totalMaterialCost + data.totalLaborCost` MUST equal `prompt04to19.directSubtotal`."
                : string.Empty;

            return $@"CROSS-PHASE VALUE LOCK (MANDATORY):
- This prompt is in Estimating/Downstream flow. Reuse these anchor values EXACTLY from Scope Review.
- Do NOT recalculate, reinterpret, round to a different value, or switch units/currency for these anchors.
- If a required output cannot match these anchors, keep your best estimate for other fields but add a clear mismatch note in any review/flags section.
{prompt25HardLock}
Anchors:
{string.Join("\n", anchors)}";
        }

        private async Task<(bool hasTotals, decimal materialTotal, decimal laborTotal, decimal directTotal)> GetPrompt04To19DirectTotalsAsync(
            int jobId
        )
        {
            var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "prompt-04-groundwork",
                "prompt-05-framing",
                "prompt-06-roofing",
                "prompt-07-exterior",
                "prompt-08-electrical",
                "prompt-09-plumbing",
                "prompt-10-hvac",
                "prompt-11-fire-protection",
                "prompt-12-insulation",
                "prompt-13-drywall",
                "prompt-14-painting",
                "prompt-15-trim",
                "prompt-16-kitchen-bath",
                "prompt-17-flooring",
                "prompt-18-exterior-flatwork",
                "prompt-19-cleaning",
            };

            var rawRows = await _context
                .JobPromptResults.AsNoTracking()
                .Where(r => r.JobId == jobId && r.RawResponse != null)
                .Select(r => new
                {
                    r.PromptKey,
                    r.RawResponse,
                    r.CreatedAt,
                })
                .ToListAsync();

            var latestByPrompt = rawRows
                .Select(r => new
                {
                    NormalizedKey = NormalizePromptKey(r.PromptKey),
                    r.RawResponse,
                    r.CreatedAt,
                })
                .Where(r => targetKeys.Contains(r.NormalizedKey))
                .GroupBy(r => r.NormalizedKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
                .ToList();

            decimal materialTotal = 0;
            decimal laborTotal = 0;

            foreach (var row in latestByPrompt)
            {
                if (
                    TryExtractDirectTotalsFromPromptResponse(
                        row.RawResponse ?? string.Empty,
                        out var material,
                        out var labor
                    )
                )
                {
                    materialTotal += material;
                    laborTotal += labor;
                }
            }

            materialTotal = Math.Round(materialTotal, 2);
            laborTotal = Math.Round(laborTotal, 2);
            var directTotal = Math.Round(materialTotal + laborTotal, 2);
            var hasTotals = directTotal > 0;
            return (hasTotals, materialTotal, laborTotal, directTotal);
        }

        private static bool TryExtractDirectTotalsFromPromptResponse(
            string response,
            out decimal materialTotal,
            out decimal laborTotal
        )
        {
            materialTotal = 0;
            laborTotal = 0;
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            var phaseBlocks = ExtractPhaseBlocks(response);
            foreach (var phaseBlock in phaseBlocks)
            {
                var outputOneTable = ExtractMarkdownTableAfterMarker(
                    phaseBlock,
                    "Output 1:",
                    "Item"
                );
                var outputTwoTable = ExtractMarkdownTableAfterMarker(
                    phaseBlock,
                    "Output 2: Subcontractor Cost Breakdown",
                    "Trade"
                ) ?? ExtractMarkdownTableAfterMarker(phaseBlock, "Output 2:", "Trade");

                var materialsByCsi = ParseMaterialTotalsByCsi(outputOneTable);
                if (materialsByCsi.Count > 0)
                {
                    materialTotal += materialsByCsi.Values.Sum(v => Math.Max(0, v));
                }

                if (outputTwoTable?.Rows?.Any() == true)
                {
                    var tradeRows = ParseOutputTwoTradeRows(outputTwoTable);
                    laborTotal += tradeRows.Sum(t => Math.Max(0, t.LaborBudget));
                }
            }

            materialTotal = Math.Round(materialTotal, 2);
            laborTotal = Math.Round(laborTotal, 2);
            return materialTotal > 0 || laborTotal > 0;
        }

        private static bool TryReadCostBreakdownDirectTotalsFromJson(
            string json,
            out decimal materialTotal,
            out decimal laborTotal
        )
        {
            materialTotal = 0;
            laborTotal = 0;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (
                    !TryGetPropertyCaseInsensitive(doc.RootElement, "data", out var data)
                    || data.ValueKind != JsonValueKind.Object
                )
                {
                    return false;
                }

                if (TryGetFiniteNumber(data, "totalMaterialCost", out var m))
                {
                    materialTotal = Math.Round((decimal)m, 2);
                }
                else if (TryGetFiniteNumber(data, "materialCost", out var mLegacy))
                {
                    materialTotal = Math.Round((decimal)mLegacy, 2);
                }

                if (TryGetFiniteNumber(data, "totalLaborCost", out var l))
                {
                    laborTotal = Math.Round((decimal)l, 2);
                }
                else if (TryGetFiniteNumber(data, "laborCost", out var lLegacy))
                {
                    laborTotal = Math.Round((decimal)lLegacy, 2);
                }

                return materialTotal > 0 || laborTotal > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryApplyPrompt25DirectTotalsAnchors(
            string json,
            decimal materialTotal,
            decimal laborTotal,
            out string harmonizedJson
        )
        {
            harmonizedJson = json;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var rootNode = JsonNode.Parse(json) as JsonObject;
                if (rootNode == null)
                {
                    return false;
                }

                var dataNode = rootNode["data"] as JsonObject;
                if (dataNode == null)
                {
                    dataNode = new JsonObject();
                    rootNode["data"] = dataNode;
                }

                dataNode["totalMaterialCost"] = Math.Round(materialTotal, 2);
                dataNode["totalLaborCost"] = Math.Round(laborTotal, 2);
                var directSubtotal = Math.Round(materialTotal + laborTotal, 2);
                dataNode["directSubtotal"] = directSubtotal;

                // Recompute downstream rollups from anchored direct totals to prevent
                // pathological model outputs (e.g., inflated pre-tax and grand totals).
                var generalConditions = ReadNonNegativeAmount(dataNode, "totalGeneralConditionsSiteServices");
                var permits = ReadNonNegativeAmount(dataNode, "totalPermitsAdminFees");
                var insuranceBonds = ReadNonNegativeAmount(dataNode, "insuranceBonds");

                var directInsurableSubtotal = Math.Round(
                    directSubtotal + generalConditions + permits + insuranceBonds,
                    2
                );
                dataNode["directInsurableSubtotal"] = directInsurableSubtotal;

                var overheadPercent = NormalizePercent(dataNode, "overheadProfitPercent");
                var contingencyPercent = NormalizePercent(dataNode, "contingencyPercent");
                var escalationPercent = NormalizePercent(dataNode, "costEscalationPercent");
                var taxPercent = NormalizePercent(dataNode, "taxPercent");

                var overheadAmount = Math.Round(directInsurableSubtotal * overheadPercent / 100m, 2);
                dataNode["overheadProfitAmount"] = overheadAmount;

                var contingencyAmount = Math.Round(
                    (directInsurableSubtotal + overheadAmount) * contingencyPercent / 100m,
                    2
                );
                dataNode["contingencyAmount"] = contingencyAmount;

                var escalationAmount = Math.Round(
                    (directInsurableSubtotal + overheadAmount + contingencyAmount)
                        * escalationPercent
                        / 100m,
                    2
                );
                dataNode["costEscalationAmount"] = escalationAmount;

                var preTaxSubtotal = Math.Round(
                    directInsurableSubtotal + overheadAmount + contingencyAmount + escalationAmount,
                    2
                );
                dataNode["preTaxSubtotal"] = preTaxSubtotal;

                var taxAmount = Math.Round(preTaxSubtotal * taxPercent / 100m, 2);
                dataNode["taxAmount"] = taxAmount;
                dataNode["grandTotalBidPrice"] = Math.Round(preTaxSubtotal + taxAmount, 2);

                harmonizedJson = rootNode.ToJsonString(
                    new JsonSerializerOptions { WriteIndented = false }
                );
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static decimal ReadNonNegativeAmount(JsonObject dataNode, string key)
        {
            if (!TryReadDecimalNode(dataNode, key, out var value))
            {
                return 0m;
            }

            return Math.Max(0m, Math.Round(value, 2));
        }

        private static decimal NormalizePercent(JsonObject dataNode, string key)
        {
            if (!TryReadDecimalNode(dataNode, key, out var value))
            {
                return 0m;
            }

            if (value < 0m)
            {
                return 0m;
            }

            // Accept either 0..1 ratio or 0..100 percentage.
            var normalized = value <= 1m ? value * 100m : value;
            return Math.Min(100m, Math.Round(normalized, 6));
        }

        private static bool TryReadDecimalNode(JsonObject obj, string key, out decimal value)
        {
            value = 0m;
            if (obj[key] is not JsonNode node)
            {
                return false;
            }

            if (node is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<decimal>(out var dec))
                {
                    value = dec;
                    return true;
                }
                if (jsonValue.TryGetValue<double>(out var dbl))
                {
                    value = (decimal)dbl;
                    return true;
                }
                if (
                    jsonValue.TryGetValue<string>(out var str)
                    && decimal.TryParse(str, out var parsed)
                )
                {
                    value = parsed;
                    return true;
                }
            }

            return false;
        }

        private static bool IsWithinDirectTotalsTolerance(decimal expected, decimal actual)
        {
            if (expected <= 0)
            {
                return false;
            }

            var absDelta = Math.Abs(expected - actual);
            var relativeTolerance = expected * DirectTotalsMismatchRejectRelativeTolerance;
            var allowed = Math.Max(DirectTotalsMismatchRejectAbsoluteTolerance, relativeTolerance);
            return absDelta <= allowed;
        }

        private static string? GetExpectedScopeSchemaVersion(string? promptKeyOrName)
        {
            var key = NormalizePromptKey(promptKeyOrName);
            if (string.Equals(key, "executive-summary-prompt", StringComparison.OrdinalIgnoreCase))
            {
                return "executive_summary.v1";
            }

            if (string.Equals(key, "prompt-21-timeline", StringComparison.OrdinalIgnoreCase))
            {
                return "timeline.v1";
            }

            if (string.Equals(key, "prompt-25-cost-breakdowns", StringComparison.OrdinalIgnoreCase))
            {
                return "cost_breakdowns.v1";
            }

            return null;
        }

        private static bool IsScopeSnapshotPromptKey(string? promptKeyOrName)
        {
            return !string.IsNullOrWhiteSpace(GetExpectedScopeSchemaVersion(promptKeyOrName));
        }

        private static string NormalizePromptKey(string? promptKeyOrName)
        {
            var key = (promptKeyOrName ?? string.Empty).Trim();

            // If a full URL or blob path is passed, keep only the final path segment.
            // Examples:
            // - https://.../prompts/prompt-25-cost-breakdowns.txt -> prompt-25-cost-breakdowns.txt
            // - prompts/prompt-25-cost-breakdowns.txt -> prompt-25-cost-breakdowns.txt
            // - prompt-25-cost-breakdowns.txt -> prompt-25-cost-breakdowns.txt
            key = key.Replace('\\', '/');
            var qIndex = key.IndexOf('?');
            if (qIndex >= 0)
            {
                key = key.Substring(0, qIndex);
            }
            var slashIndex = key.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < key.Length - 1)
            {
                key = key.Substring(slashIndex + 1);
            }

            if (key.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                key = key[..^4];
            }
            return key;
        }

        private static bool TryValidateScopePromptJson(
            string? promptKeyOrName,
            string json,
            string? schemaVersion,
            out string reason
        )
        {
            reason = string.Empty;
            var expected = GetExpectedScopeSchemaVersion(promptKeyOrName);
            if (string.IsNullOrWhiteSpace(expected))
            {
                return true;
            }

            if (!string.Equals(schemaVersion, expected, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"schemaVersion mismatch. expected={expected}, actual={schemaVersion ?? "<null>"}";
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = "root is not a JSON object";
                    return false;
                }

                if (
                    !root.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Object
                )
                {
                    reason = "missing object field: data";
                    return false;
                }

                if (
                    string.Equals(expected, "executive_summary.v1", StringComparison.OrdinalIgnoreCase)
                )
                {
                    if (!TryGetPropertyCaseInsensitive(data, "overview", out var overview))
                    {
                        reason = "missing required field: data.overview";
                        return false;
                    }
                    if (overview.ValueKind != JsonValueKind.String)
                    {
                        reason = "invalid type for data.overview";
                        return false;
                    }
                    if (
                        !TryGetPropertyCaseInsensitive(data, "keyHighlights", out var highlights)
                        || highlights.ValueKind != JsonValueKind.Array
                    )
                    {
                        reason = "missing required array: data.keyHighlights";
                        return false;
                    }
                    return true;
                }

                if (string.Equals(expected, "timeline.v1", StringComparison.OrdinalIgnoreCase))
                {
                    if (
                        !TryGetPropertyCaseInsensitive(data, "tasks", out var tasks)
                        || tasks.ValueKind != JsonValueKind.Array
                        || tasks.GetArrayLength() == 0
                    )
                    {
                        reason = "missing required non-empty array: data.tasks";
                        return false;
                    }

                    foreach (var task in tasks.EnumerateArray())
                    {
                        if (task.ValueKind != JsonValueKind.Object)
                        {
                            reason = "invalid task row in data.tasks";
                            return false;
                        }

                        if (
                            !TryGetPropertyCaseInsensitive(task, "taskId", out _)
                            || !TryGetPropertyCaseInsensitive(task, "task", out _)
                            || !TryGetPropertyCaseInsensitive(task, "startDate", out _)
                            || !TryGetPropertyCaseInsensitive(task, "endDate", out _)
                        )
                        {
                            reason = "task row missing one of: taskId/task/startDate/endDate";
                            return false;
                        }
                    }
                    return true;
                }

                if (string.Equals(expected, "cost_breakdowns.v1", StringComparison.OrdinalIgnoreCase))
                {
                    if (
                        !TryGetFiniteNumber(data, "totalMaterialCost", out var material)
                        || !TryGetFiniteNumber(data, "totalLaborCost", out var labor)
                        || !TryGetFiniteNumber(data, "totalGeneralConditionsSiteServices", out var general)
                        || !TryGetFiniteNumber(data, "totalPermitsAdminFees", out var permits)
                        || !TryGetFiniteNumber(data, "insuranceBonds", out var insurance)
                        || !TryGetFiniteNumber(data, "directInsurableSubtotal", out var directSubtotal)
                        || !TryGetFiniteNumber(data, "grandTotalBidPrice", out _)
                    )
                    {
                        reason =
                            "missing required numeric field in cost_breakdowns.v1 (materials/labor/general/permits/insurance/direct/grandTotal)";
                        return false;
                    }

                    var expectedDirect = material + labor + general + permits + insurance;
                    if (Math.Abs(expectedDirect - directSubtotal) > 2.5)
                    {
                        reason =
                            $"directInsurableSubtotal does not reconcile (expected {expectedDirect:F2}, actual {directSubtotal:F2})";
                        return false;
                    }
                    return true;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = $"json parse/validation error: {ex.Message}";
                return false;
            }
        }

        private static bool TryGetPropertyCaseInsensitive(
            JsonElement obj,
            string propertyName,
            out JsonElement value
        )
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static bool TryGetFiniteNumber(
            JsonElement obj,
            string propertyName,
            out double number
        )
        {
            number = 0;
            if (!TryGetPropertyCaseInsensitive(obj, propertyName, out var value))
            {
                return false;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.TryGetDouble(out number) && double.IsFinite(number);
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return double.TryParse(value.GetString(), out number) && double.IsFinite(number);
            }

            return false;
        }

        private static bool TryGetGrandTotalBidPrice(string? json, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (
                    !TryGetPropertyCaseInsensitive(root, "data", out var data)
                    || data.ValueKind != JsonValueKind.Object
                )
                {
                    return false;
                }

                return TryGetFiniteNumber(data, "grandTotalBidPrice", out value);
            }
            catch
            {
                return false;
            }
        }

        private async Task TrySendAnalysisProgressUpdate(
            string? connectionId,
            Middleware.AnalysisProgressUpdate update
        )
        {
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return;
            }

            try
            {
                await _hubContext
                    .Clients.Client(connectionId)
                    .SendAsync("ReceiveAnalysisProgress", update);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Non-fatal SignalR progress send failure for JobId={JobId} ConnectionId={ConnectionId}",
                    update.JobId,
                    connectionId
                );
            }
        }

        private async Task EnsureScopeSnapshotCompletenessAsync(int jobId, DateTime runStartedAtUtc)
        {
            var required = new[]
            {
                ("executive-summary-prompt", "executive_summary.v1"),
                ("prompt-21-timeline", "timeline.v1"),
                ("prompt-25-cost-breakdowns", "cost_breakdowns.v1"),
            };

            var failures = new List<string>();
            foreach (var (promptKey, expectedSchema) in required)
            {
                var rows = await _context
                    .JobPromptResults.AsNoTracking()
                    .Where(r => r.JobId == jobId && r.PromptKey == promptKey && r.CreatedAt >= runStartedAtUtc)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(
                        r => new
                        {
                            r.ParsedJson,
                            r.SchemaVersion,
                            r.RawResponse,
                            r.CreatedAt,
                        }
                    )
                    .ToListAsync();

                var accepted = rows.FirstOrDefault(r => r.ParsedJson != null);
                if (accepted == null)
                {
                    var latest = rows.FirstOrDefault();
                    var reason =
                        TryExtractRejectionReason(latest?.RawResponse)
                        ?? "No accepted parsed JSON row was written in this run.";
                    failures.Add($"{promptKey}: {reason}");
                    continue;
                }

                if (!string.Equals(accepted.SchemaVersion, expectedSchema, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add(
                        $"{promptKey}: schema mismatch. expected {expectedSchema}, got {accepted.SchemaVersion ?? "<null>"}."
                    );
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "Scope snapshot persistence failed: " + string.Join(" | ", failures)
                );
            }
        }

        private static string BuildRejectedRawResponse(string response, string reason)
        {
            var safeReason = string.IsNullOrWhiteSpace(reason) ? "rejected" : reason.Trim();
            var safeResponse = response ?? string.Empty;
            return $"{RejectionReasonPrefix} {safeReason}\n\n{safeResponse}";
        }

        internal static string? TryExtractRejectionReason(string? rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return null;
            }

            if (!rawResponse.StartsWith(RejectionReasonPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var lineEnd = rawResponse.IndexOf('\n');
            var firstLine = lineEnd >= 0 ? rawResponse.Substring(0, lineEnd) : rawResponse;
            var reason = firstLine.Replace(RejectionReasonPrefix, string.Empty).Trim();
            return string.IsNullOrWhiteSpace(reason) ? "Rejected by validation." : reason;
        }

        private static string BuildPhaseInstructionPrefix(int phaseNumber, string phaseTitle)
        {
            var safePhase = Math.Max(1, phaseNumber);
            var safeTitle = string.IsNullOrWhiteSpace(phaseTitle) ? "Analysis" : phaseTitle.Trim();
            return $@"CRITICAL INSTRUCTION (follow exactly):
- You are now producing output for Phase {safePhase}: {safeTitle}.
- Do NOT output any JSON metadata block in this response.
- Do NOT repeat any earlier phases.
- Start your response with the exact title line: ### Phase {safePhase}: {safeTitle}
- Continue with the requested content immediately after that title.
";
        }

        private static string EnsurePhaseHeading(
            string? rawResponse,
            int phaseNumber,
            string phaseTitle
        )
        {
            var safePhase = Math.Max(1, phaseNumber);
            var safeTitle = string.IsNullOrWhiteSpace(phaseTitle) ? "Analysis" : phaseTitle.Trim();
            var canonicalHeading = $"### Phase {safePhase}: {safeTitle}";
            var response = (rawResponse ?? string.Empty).TrimStart();

            if (string.IsNullOrWhiteSpace(response))
            {
                return canonicalHeading;
            }

            var phaseHeadingRegex = new Regex(
                @"^\s*#{2,6}\s*Phase\s+\d+\s*:\s*.*$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase
            );
            var firstHeadingMatch = phaseHeadingRegex.Match(response);

            if (firstHeadingMatch.Success)
            {
                response = phaseHeadingRegex.Replace(response, canonicalHeading, 1);
            }
            else
            {
                response = $"{canonicalHeading}\n\n{response}";
            }

            // Prevent accidental extra top-level phase headings in a single model response.
            // These can make the final report appear to have duplicated phases.
            var lines = response.Split('\n');
            for (var i = 1; i < lines.Length; i++)
            {
                var extraHeadingMatch = Regex.Match(
                    lines[i],
                    @"^\s*#{2,6}\s*Phase\s+\d+\s*:\s*(.*)$",
                    RegexOptions.IgnoreCase
                );
                if (!extraHeadingMatch.Success)
                {
                    continue;
                }

                var extraTitle = extraHeadingMatch.Groups[1].Value.Trim();
                lines[i] = string.IsNullOrWhiteSpace(extraTitle)
                    ? "#### Additional Details"
                    : $"#### {extraTitle}";
            }

            return string.Join("\n", lines);
        }

        private static bool TryExtractFirstPhaseTitle(string content, out string phaseTitle)
        {
            phaseTitle = string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            var match = Regex.Match(
                content,
                @"^\s*#{2,6}\s*Phase\s+\d+\s*:\s*(.*)$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase
            );
            if (!match.Success)
            {
                return false;
            }

            phaseTitle = string.IsNullOrWhiteSpace(match.Groups[1].Value)
                ? "Analysis"
                : match.Groups[1].Value.Trim();
            return true;
        }

        private async Task UpdateAnalysisState(
            int jobId,
            string statusMessage,
            int currentStep,
            int totalSteps,
            bool isComplete = false,
            bool hasFailed = false,
            string errorMessage = null
        )
        {
            try
            {
                var state = await _context.JobAnalysisStates.FirstOrDefaultAsync(s =>
                    s.JobId == jobId
                );
                if (state == null)
                {
                    state = new JobAnalysisState { JobId = jobId };
                    _context.JobAnalysisStates.Add(state);
                }

                state.StatusMessage = statusMessage;
                state.CurrentStep = currentStep;
                state.TotalSteps = totalSteps;
                state.IsComplete = isComplete;
                state.HasFailed = hasFailed;
                state.ErrorMessage = errorMessage;
                state.LastUpdated = DateTime.UtcNow;

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update analysis state for Job {JobId}", jobId);
            }
        }

        private async Task UpdateAnalysisData(int jobId, string dataType, object data)
        {
            try
            {
                var existing = await _context.JobAnalysisStates.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.JobId == jobId);

                if (existing == null)
                {
                    _context.JobAnalysisStates.Add(
                        new JobAnalysisState
                        {
                            JobId = jobId,
                            ExtractedDataJson = "{}",
                            LastUpdated = DateTime.UtcNow
                        }
                    );
                    await _context.SaveChangesAsync();
                }

                var jsonValue = JsonSerializer.Serialize(data);
                var path = dataType.All(c => char.IsLetterOrDigit(c) || c == '_')
                    ? $"$.{dataType}"
                    : $"$[\"{dataType}\"]";

                await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [JobAnalysisStates]
SET [ExtractedDataJson] = JSON_MODIFY(
        COALESCE(NULLIF([ExtractedDataJson], ''), '{{}}'),
        {path},
        CASE
            WHEN LEFT(LTRIM({jsonValue}), 1) IN ('{{', '[') THEN JSON_QUERY({jsonValue})
            ELSE {jsonValue}
        END
    ),
    [LastUpdated] = SYSUTCDATETIME()
WHERE [JobId] = {jobId};
");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update analysis data for Job {JobId}", jobId);
            }
        }
    }
}

