using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Models.Enums;

namespace ProbuildBackend.Services
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
        private const string RenovationAnalysisPersonaKey = "ProBuildAI_Renovation_Prompt.txt";
        private const string FailureCorrectiveActionKey = "prompt-failure-corrective-action.txt";

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

                int step = 1;
                foreach (var promptKey in orderedPrompts)
                {
                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        await _hubContext
                            .Clients.Client(connectionId)
                            .SendAsync(
                                "ReceiveAnalysisProgress",
                                new Middleware.AnalysisProgressUpdate
                                {
                                    JobId = job.Id,
                                    StatusMessage =
                                        $"Analyzing: {promptKey.Replace(".txt", "").Replace("-", " ")}",
                                    CurrentStep = step,
                                    TotalSteps = orderedPrompts.Count,
                                    IsComplete = false,
                                    HasFailed = false,
                                }
                            );
                    }

                    await UpdateAnalysisState(
                        job.Id,
                        $"Analyzing: {promptKey.Replace(".txt", "").Replace("-", " ")}",
                        step,
                        orderedPrompts.Count
                    );

                    var subPrompt = await _promptManager.GetPromptAsync(null, promptKey);
                    var (analysisResult, _) = await _aiService.ContinueConversationAsync(
                        conversationId,
                        userId,
                        subPrompt,
                        requestDto.DocumentUrls,
                        true,
                        personaWithoutJson
                    );
                    var message = new Message
                    {
                        ConversationId = conversationId,
                        Role = "model",
                        Content = analysisResult,
                        Timestamp = DateTime.UtcNow,
                    };
                    await _conversationRepo.AddMessageAsync(message);
                    reportBuilder.Append("\n\n---\n\n");
                    reportBuilder.Append(analysisResult);

                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        await ParseAndBroadcastPromptResult(
                            job.Id,
                            promptKey,
                            analysisResult,
                            connectionId
                        );
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
                }

                if (job != null && generateDetailsWithAi)
                {
                    await ParseAndSaveAiJobDetails(job.Id, reportBuilder.ToString());
                }

                var completedJob = await _context.Jobs.FindAsync(job.Id);
                if (completedJob != null)
                {
                    completedJob.Status = "PRELIMINARY";
                    await _context.SaveChangesAsync();
                }

                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext
                        .Clients.Client(connectionId)
                        .SendAsync(
                            "ReceiveAnalysisProgress",
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
                }

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
                if (!string.IsNullOrEmpty(connectionId))
                {
                    var jobForError = await _context.Jobs.FindAsync(requestDto.JobId);
                    if (jobForError != null)
                    {
                        await _hubContext
                            .Clients.Client(connectionId)
                            .SendAsync(
                                "ReceiveAnalysisProgress",
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
                }
                throw;
            }
            finally
            {
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
                var (initialResponse, conversationId) =
                    await _aiService.StartMultimodalConversationAsync(
                        userId,
                        documentUris,
                        systemPersonaPrompt,
                        initialUserPrompt,
                        jobDetails.ConversationId
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
                    connectionId
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
                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext
                        .Clients.Client(connectionId)
                        .SendAsync(
                            "ReceiveAnalysisProgress",
                            new Middleware.AnalysisProgressUpdate
                            {
                                JobId = jobDetails.Id,
                                StatusMessage =
                                    "Analysis failed - we're sorry for the inconvenience",
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
                var (initialResponse, conversationId) =
                    await _aiService.StartMultimodalConversationAsync(
                        userId,
                        documentUris,
                        personaPrompt,
                        initialUserPrompt,
                        jobDetails.ConversationId
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
                    connectionId
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
                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext
                        .Clients.Client(connectionId)
                        .SendAsync(
                            "ReceiveAnalysisProgress",
                            new Middleware.AnalysisProgressUpdate
                            {
                                JobId = jobDetails.Id,
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
            string? connectionId
        )
        {
            var stringBuilder = new StringBuilder();
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
                "executive-summary-prompt",
            };

            try
            {
                string lastResponse;
                int step = 1;
                foreach (var promptName in promptNames)
                {
                    _logger.LogInformation(
                        "Executing step {Step} of {TotalSteps}: {PromptName} for conversation {ConversationId}",
                        step,
                        promptNames.Length,
                        promptName,
                        conversationId
                    );

                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        await _hubContext
                            .Clients.Client(connectionId)
                            .SendAsync(
                                "ReceiveAnalysisProgress",
                                new Middleware.AnalysisProgressUpdate
                                {
                                    JobId = jobId,
                                    StatusMessage =
                                        $"Analyzing: {promptName.Replace(".txt", "").Replace("-", " ")}",
                                    CurrentStep = step,
                                    TotalSteps = promptNames.Length,
                                    IsComplete = false,
                                    HasFailed = false,
                                }
                            );
                    }

                    await UpdateAnalysisState(
                        jobId,
                        $"Analyzing: {promptName.Replace(".txt", "").Replace("-", " ")}",
                        step,
                        promptNames.Length
                    );

                    var promptText = await _promptManager.GetPromptAsync("", $"{promptName}.txt");
                    (lastResponse, _) = await _aiService.ContinueConversationAsync(
                        conversationId,
                        userId,
                        promptText,
                        null,
                        true
                    );

                    await _conversationRepo.AddMessageAsync(
                        new Message
                        {
                            ConversationId = conversationId,
                            Role = "model",
                            Content = lastResponse,
                        }
                    );

                    stringBuilder.Append("\n\n---\n\n");
                    stringBuilder.Append(lastResponse);

                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        await ParseAndBroadcastPromptResult(
                            jobId,
                            promptName,
                            lastResponse,
                            connectionId
                        );
                    }

                    step++;
                }

                var job = await _context.Jobs.FindAsync(jobId);
                if (job != null)
                {
                    job.Status = "PRELIMINARY";
                    await _context.SaveChangesAsync();
                }

                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext
                        .Clients.Client(connectionId)
                        .SendAsync(
                            "ReceiveAnalysisProgress",
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
                }

                _logger.LogInformation(
                    "Full sequential analysis completed successfully for conversation {ConversationId}",
                    conversationId
                );
                return stringBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An error occurred during sequential prompt execution for conversation {ConversationId}",
                    conversationId
                );
                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext
                        .Clients.Client(connectionId)
                        .SendAsync(
                            "ReceiveAnalysisProgress",
                            new Middleware.AnalysisProgressUpdate
                            {
                                JobId = jobId,
                                StatusMessage = "Analysis failed during sequential execution.",
                                IsComplete = false,
                                HasFailed = true,
                                ErrorMessage = ex.Message,
                            }
                        );
                }
                throw;
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

        private async Task ParseAndBroadcastPromptResult(
            int jobId,
            string promptName,
            string response,
            string connectionId
        )
        {
            try
            {
                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning(
                        "ParseAndBroadcastPromptResult: Response is empty for prompt: {PromptName}",
                        promptName
                    );
                    return;
                }

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
                        @"\| Room Name \| Area.*?(\n\n|###|$)",
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
                        @"\| Category \| Details.*?(\n\n|###|$)",
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
                        @"\| Permit Name \| Issuing Agency \| Requirements.*?(\n\n|###|$)",
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
                        @"\| Sheet Number \| Error Description \| Potential Impact.*?(\n\n|###|$)",
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
                        @"### \d+\. Zoning & Site Report([\s\S]*?)###",
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
                            @"Utility Status:(.*?)(\n\* [^*]|\n###)",
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
                            @"\*\*Unforeseen Work.*?\*\*:(.*?)(?=\n\*|\n###|$)",
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
                        @"\| PPE Item \|.*?(\n\n|###|$)",
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
                        @"\| Mock-up Description \|.*?(\n\n|###|$)",
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
                        @"\| Phase \| Activity.*?Hold Point\?.*?(\n\n|###|$)",
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
                    await UpdateAnalysisData(jobId, "quality-management", quality);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    $"Error parsing and broadcasting prompt result for {promptName}"
                );
            }
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
                var tradePackages = new List<TradePackage>();

                // Regex to find all "Subcontractor Cost Breakdown" tables
                var tableRegex = new Regex(
                    @"### Output 2: Subcontractor Cost Breakdown[\s\S]*?\| Trade \| Scope of Work \|[\s\S]*?(\n\n|###|$)",
                    RegexOptions.Singleline
                );

                var matches = tableRegex.Matches(aiResponse);

                foreach (Match match in matches)
                {
                    var tableContent = match.Value;
                    var rows = tableContent.Split('\n');

                    foreach (var row in rows)
                    {
                        if (
                            string.IsNullOrWhiteSpace(row)
                            || !row.StartsWith("|")
                            || row.Contains("Trade")
                            || row.Contains("---")
                        )
                            continue;

                        var cols = row.Split('|')
                            .Select(c => c.Trim())
                            .Where(c => !string.IsNullOrEmpty(c))
                            .ToList();

                        // Expected Columns:
                        // 0: Trade
                        // 1: Scope of Work
                        // 2: Estimated Man-Hours
                        // 3: Localized Hourly Rate
                        // 4: Total Estimated Cost
                        // 5: CSI MasterFormat Code
                        // 6: Estimated Cost per Area (sq ft)

                        if (cols.Count >= 5)
                        {
                            var tradeName = cols[0];
                            if (tradeName.Contains("Total Subcontractor Cost"))
                                continue;

                            var scope = cols[1];
                            decimal.TryParse(cols[2], out var manHours);

                            var hourlyRateStr = cols[3].Replace("$", "").Replace(",", "");
                            decimal.TryParse(hourlyRateStr, out var hourlyRate);

                            var totalCostStr = cols[4].Replace("$", "").Replace(",", "");
                            decimal.TryParse(totalCostStr, out var totalCost);

                            var csiCode = cols.Count > 5 ? cols[5] : null;

                            // Determine category based on keywords or default to Trade
                            var category = "Trade";
                            if (tradeName.Contains("Supplier") || tradeName.Contains("Provider"))
                                category = "Supplier";
                            if (tradeName.Contains("Rental") || tradeName.Contains("Equipment"))
                                category = "Equipment";

                            tradePackages.Add(
                                new TradePackage
                                {
                                    JobId = jobId,
                                    TradeName = tradeName,
                                    ScopeOfWork = scope,
                                    EstimatedManHours = manHours,
                                    HourlyRate = hourlyRate,
                                    Budget = totalCost,
                                    CsiCode = csiCode,
                                    Category = category,
                                    Status = "Draft",
                                    PostedToMarketplace = false,
                                    EstimatedDuration = "TBD", // Could parse timeline if needed
                                }
                            );
                        }
                    }
                }

                if (tradePackages.Any())
                {
                    // Clear existing packages for this job to avoid duplicates on re-runs
                    var existing = _context.TradePackages.Where(tp => tp.JobId == jobId);
                    _context.TradePackages.RemoveRange(existing);

                    _context.TradePackages.AddRange(tradePackages);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation(
                        $"Saved {tradePackages.Count} trade packages for Job {jobId}"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to parse Trade Packages for Job {jobId}");
            }
        }

        private async Task<string> ExecuteSequentialRenovationPromptsAsync(
            string conversationId,
            string userId,
            string initialResponse,
            int jobId,
            string? connectionId
        )
        {
            var stringBuilder = new StringBuilder();
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
                int step = 1;
                foreach (var promptName in promptNames)
                {
                    _logger.LogInformation(
                        "Executing step {Step} of {TotalSteps}: {PromptName} for conversation {ConversationId}",
                        step,
                        promptNames.Length,
                        promptName,
                        conversationId
                    );

                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        await _hubContext
                            .Clients.Client(connectionId)
                            .SendAsync(
                                "ReceiveAnalysisProgress",
                                new Middleware.AnalysisProgressUpdate
                                {
                                    JobId = jobId,
                                    StatusMessage =
                                        $"Analyzing: {promptName.Replace(".txt", "").Replace("-", " ")}",
                                    CurrentStep = step,
                                    TotalSteps = promptNames.Length,
                                    IsComplete = false,
                                    HasFailed = false,
                                }
                            );
                    }

                    await UpdateAnalysisState(
                        jobId,
                        $"Analyzing: {promptName.Replace(".txt", "").Replace("-", " ")}",
                        step,
                        promptNames.Length
                    );

                    var promptText = await _promptManager.GetPromptAsync(
                        "RenovationPrompts/",
                        $"{promptName}.txt"
                    );
                    (lastResponse, _) = await _aiService.ContinueConversationAsync(
                        conversationId,
                        userId,
                        promptText,
                        null,
                        true
                    );

                    await _conversationRepo.AddMessageAsync(
                        new Message
                        {
                            ConversationId = conversationId,
                            Role = "model",
                            Content = lastResponse,
                        }
                    );

                    stringBuilder.Append("\n\n---\n\n");
                    stringBuilder.Append(lastResponse);

                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        await ParseAndBroadcastPromptResult(
                            jobId,
                            promptName,
                            lastResponse,
                            connectionId
                        );
                    }

                    step++;
                }

                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext
                        .Clients.Client(connectionId)
                        .SendAsync(
                            "ReceiveAnalysisProgress",
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
                }

                _logger.LogInformation(
                    "Full sequential renovation analysis completed successfully for conversation {ConversationId}",
                    conversationId
                );
                return stringBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An error occurred during sequential renovation prompt execution for conversation {ConversationId}",
                    conversationId
                );
                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext
                        .Clients.Client(connectionId)
                        .SendAsync(
                            "ReceiveAnalysisProgress",
                            new Middleware.AnalysisProgressUpdate
                            {
                                JobId = jobId,
                                StatusMessage = "Analysis failed during sequential execution.",
                                IsComplete = false,
                                HasFailed = true,
                                ErrorMessage = ex.Message,
                            }
                        );
                }
                throw;
            }
        }

        public async Task<string> AnalyzeBidsAsync(List<BidModel> bids, string comparisonType)
        {
            _keepAliveService.StartPinging();
            try
            {
                string promptKey = comparisonType.Equals(
                    "Vendor",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "vendor-comparison-prompt.txt"
                    : "subcontractor-comparison-prompt.txt";

                var prompt = await _promptManager.GetPromptAsync("ComparisonPrompts/", promptKey);

                var bidsDetails = new List<object>();
                foreach (var bid in bids)
                {
                    string quoteText = bid.Task ?? "No detailed quote text provided.";
                    if (!string.IsNullOrEmpty(bid.QuoteId))
                    {
                        var quote = await _context
                            .Quotes.Include(q => q.Rows)
                            .FirstOrDefaultAsync(q => q.Id == bid.QuoteId);
                        if (quote != null)
                        {
                            quoteText = JsonSerializer.Serialize(quote);
                        }
                    }
                    else if (!string.IsNullOrEmpty(bid.DocumentUrl))
                    {
                        try
                        {
                            var (contentStream, _, _) = await _azureBlobService.GetBlobContentAsync(
                                bid.DocumentUrl
                            );
                            quoteText = await _pdfTextExtractionService.ExtractTextAsync(
                                contentStream
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Failed to extract text from PDF for bid {BidId}",
                                bid.Id
                            );
                            quoteText = "Error extracting text from PDF.";
                        }
                    }

                    bidsDetails.Add(
                        new
                        {
                            bid.Id,
                            bid.Amount,
                            bid.User?.ProbuildRating,
                            bid.User?.GoogleRating,
                            bid.User?.JobPreferences,
                            QuoteDetails = quoteText,
                        }
                    );
                }

                var bidsJson = JsonSerializer.Serialize(bidsDetails);
                var fullPrompt = $"{prompt}\n\nBids:\n{bidsJson}";

                var (analysisResult, _) = await _aiService.StartMultimodalConversationAsync(
                    "system-user",
                    null,
                    fullPrompt,
                    $"Analyze the provided {comparisonType} bids and return the top 3 candidates."
                );

                return analysisResult;
            }
            finally
            {
                _keepAliveService.StopPinging();
            }
        }

        public async Task<string> GenerateFeedbackForUnsuccessfulBidderAsync(
            BidModel bid,
            BidModel winningBid
        )
        {
            var user = bid.User;
            if (user != null)
            {
                bool isFreeTier =
                    user.SubscriptionPackage == "Basic (Free) ($0.00)"
                    || string.IsNullOrEmpty(user.SubscriptionPackage)
                    || user.SubscriptionPackage.ToUpper() == "BASIC";

                if (isFreeTier)
                {
                    return "Feedback reports are only available for users on a paid subscription tier.";
                }
            }

            var prompt = await _promptManager.GetPromptAsync(
                "ComparisonPrompts/",
                "unsuccessful-bid-prompt.txt"
            );

            var unsuccessfulQuote = await _context.Quotes.FirstOrDefaultAsync(q =>
                q.Id == bid.QuoteId
            );
            var winningQuote = await _context.Quotes.FirstOrDefaultAsync(q =>
                q.Id == winningBid.QuoteId
            );

            var unsuccessfulBidAnalysis = new
            {
                bid.Id,
                bid.Amount,
                QuoteDetails = unsuccessfulQuote,
            };

            var winningBidBenchmark = new { winningBid.Amount, QuoteDetails = winningQuote };

            var promptInput = new
            {
                ProjectName = bid.Job?.ProjectName,
                WorkPackage = bid.Job?.JobType,
                OurCompanyName = "Probuild", // Or fetch dynamically
                UnsuccessfulSubcontractorName = user?.UserName,
                AnalysisOfUnsuccessfulQuotation = JsonSerializer.Serialize(unsuccessfulBidAnalysis),
                WinningBidBenchmark = JsonSerializer.Serialize(winningBidBenchmark),
            };

            var fullPrompt = prompt
                .Replace(
                    "[e.g., The Falcon Heights Residential Development]",
                    promptInput.ProjectName
                )
                .Replace(
                    "[e.g., Structural Steel Fabrication and Erection]",
                    promptInput.WorkPackage
                )
                .Replace("[Your Company Name]", promptInput.OurCompanyName)
                .Replace(
                    "[Enter the name of the company you are writing to]",
                    promptInput.UnsuccessfulSubcontractorName
                )
                .Replace(
                    "[Paste the detailed analysis of the subcontractor's quote here, including price, schedule, inclusions, exclusions, compliance notes.]",
                    promptInput.AnalysisOfUnsuccessfulQuotation
                )
                .Replace(
                    "[Summarize the key advantages of the winning bid. For example: \"Final price was R 1,150,000 (8% lower). Proposed schedule was 16 weeks (2 weeks shorter). Fully compliant with specifications. Included a detailed plan for managing material price volatility.\"]",
                    promptInput.WinningBidBenchmark
                );

            var (analysisResult, _) = await _aiService.StartMultimodalConversationAsync(
                "system-user",
                null,
                fullPrompt,
                "Generate a feedback report for the provided bid."
            );

            return analysisResult;
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
                var state = await _context.JobAnalysisStates.FirstOrDefaultAsync(s =>
                    s.JobId == jobId
                );
                if (state == null)
                {
                    state = new JobAnalysisState { JobId = jobId };
                    _context.JobAnalysisStates.Add(state);
                }

                var currentData = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(state.ExtractedDataJson))
                {
                    try
                    {
                        currentData =
                            JsonSerializer.Deserialize<Dictionary<string, object>>(
                                state.ExtractedDataJson
                            ) ?? new Dictionary<string, object>();
                    }
                    catch { }
                }

                currentData[dataType] = data;

                state.ExtractedDataJson = JsonSerializer.Serialize(currentData);
                state.LastUpdated = DateTime.UtcNow;

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update analysis data for Job {JobId}", jobId);
            }
        }
    }
}
