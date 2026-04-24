using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using BuildigBackend.Interface;
using BuildigBackend.Middleware;
using BuildigBackend.Models;
using BuildigBackend.Models.DTO;
using IEmailSender = BuildigBackend.Interface.IEmailSender;

namespace BuildigBackend.Services
{
    public class DocumentProcessorService : IDocumentProcessorService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ProgressHub> _hubContext;
        private readonly IEmailSender _emailService;
        private readonly IAiAnalysisService _aiAnalysisService;
        private readonly IConversationRepository _conversationRepository;
        private readonly AzureBlobService _azureBlobService;
        private readonly IEmailTemplateService _emailTemplate;
        private readonly IJobSubtaskTimelineSyncService _jobSubtaskTimelineSync;

        public DocumentProcessorService(
            ApplicationDbContext context,
            IHubContext<ProgressHub> hubContext,
            IEmailSender emailService,
            IAiAnalysisService aiAnalysisService,
            IConversationRepository conversationRepository,
            AzureBlobService azureBlobService,
            IEmailTemplateService emailTemplate,
            IJobSubtaskTimelineSyncService jobSubtaskTimelineSync
        )
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _aiAnalysisService =
                aiAnalysisService ?? throw new ArgumentNullException(nameof(aiAnalysisService));
            _conversationRepository =
                conversationRepository
                ?? throw new ArgumentNullException(nameof(conversationRepository));
            _azureBlobService = azureBlobService;
            _emailTemplate = emailTemplate;
            _jobSubtaskTimelineSync =
                jobSubtaskTimelineSync
                ?? throw new ArgumentNullException(nameof(jobSubtaskTimelineSync));
        }

        [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public async Task ProcessDocumentsForJobAsync(
            int jobId,
            List<string> documentUrls,
            string connectionId,
            bool generateDetailsWithAi,
            string userContextText,
            string userContextFileUrl,
            string budgetLevel,
            bool? isFrontEnd = false,
            string? ipAddress = null
        )
        {
            try
            {
                var job = await _context.Jobs.FindAsync(jobId);
                if (job == null)
                {
                    throw new InvalidOperationException($"Job with ID {jobId} not found.");
                }
                using var userAnalysisLock = AcquireUserAnalysisLock(job.UserId);
                using var jobLock = JobStorage
                    .Current.GetConnection()
                    .AcquireDistributedLock(
                        $"analysis:comprehensive:job:{jobId}",
                        TimeSpan.FromHours(1)
                    );

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == job.UserId);
                if (user == null)
                {
                    Console.WriteLine($"User with ID {job.UserId} not found. Cannot send email.");
                }

                string finalReport = await _aiAnalysisService.PerformComprehensiveAnalysisAsync(
                    job.UserId,
                    documentUrls,
                    job,
                    generateDetailsWithAi,
                    userContextText,
                    userContextFileUrl,
                    budgetLevel,
                    connectionId
                );
                await EnsureScopePromptResultsPersistedAsync(jobId, finalReport);

                var processingResult = new DocumentProcessingResult
                {
                    JobId = jobId,
                    DocumentId = 0, // This might need re-evaluation if we need to link to a specific document.
                    BomJson = JsonSerializer.Serialize(""), // Placeholder, as analysis service handles this.
                    MaterialsEstimateJson = JsonSerializer.Serialize(""), // Placeholder.
                    FullResponse = finalReport,
                    CreatedAt = DateTime.UtcNow,
                };

                _context.DocumentProcessingResults.Add(processingResult);
                await _jobSubtaskTimelineSync.ReplaceSubtasksFromReportAsync(jobId, finalReport);
                await _context.SaveChangesAsync();

                job.Status = "PRELIMINARY";

                var analysisState = await _context.JobAnalysisStates.FirstOrDefaultAsync(s =>
                    s.JobId == jobId
                );

                if (analysisState == null)
                {
                    analysisState = new JobAnalysisState { JobId = jobId };
                    _context.JobAnalysisStates.Add(analysisState);
                }

                analysisState.StatusMessage = "Analysis complete.";
                analysisState.CurrentStep = 1;
                analysisState.TotalSteps = 1;
                analysisState.IsComplete = true;
                analysisState.HasFailed = false;
                analysisState.ErrorMessage = null;
                analysisState.LastUpdated = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                if (isFrontEnd == true && !string.IsNullOrEmpty(ipAddress))
                {
                    var existing = await _context.WebsiteJobTracker.FirstOrDefaultAsync(x =>
                        x.IpAddress == ipAddress
                    );

                    if (existing == null)
                    {
                        _context.WebsiteJobTracker.Add(
                            new WebsiteJobTrackerModel
                            {
                                Id = Guid.NewGuid(),
                                IpAddress = ipAddress,
                                JobCount = 1,
                                FirstSeenAt = DateTime.UtcNow,
                                LastSeenAt = DateTime.UtcNow,
                            }
                        );
                    }
                    else
                    {
                        existing.JobCount++;
                        existing.LastSeenAt = DateTime.UtcNow;
                    }

                    await _context.SaveChangesAsync();
                }
                if (user != null)
                {
                    var ProjectAnalysisEmail = await _emailTemplate.GetTemplateAsync(
                        "ProjectAnalysisReadyEmail"
                    );

                    var jobAddress = await _context
                        .JobAddresses.Where(j => j.JobId == job.Id)
                        .FirstOrDefaultAsync();
                    var jobDocument = await _context
                        .JobDocuments.Where(d => d.JobId == job.Id)
                        .ToListAsync();
                    var frontendUrl =
                        Environment.GetEnvironmentVariable("FRONTEND_URL")
                        ?? "http://localhost:4200";

                    var query = HttpUtility.ParseQueryString(string.Empty);
                    query["jobId"] = job.Id.ToString();
                    query["operatingArea"] = job.OperatingArea;
                    query["address"] = job.Address;
                    query["projectName"] = job.ProjectName;
                    query["jobType"] = job.JobType;
                    query["buildingSize"] = job.BuildingSize.ToString();
                    query["wallStructure"] = job.WallStructure;
                    query["wallInsulation"] = job.WallInsulation;
                    query["roofStructure"] = job.RoofStructure;
                    query["roofInsulation"] = job.RoofInsulation;
                    query["electricalSupply"] = job.ElectricalSupplyNeeds;
                    query["finishes"] = job.Finishes;
                    query["foundation"] = job.Foundation;
                    query["date"] = job.DesiredStartDate.ToString("MM/dd/yyyy");
                    query["documents"] = jobDocument.Any()
                        ? string.Join(",", jobDocument.Select(d => d.Id))
                        : "";
                    query["latitude"] = jobAddress?.Latitude?.ToString() ?? "";
                    query["longitude"] = jobAddress?.Longitude?.ToString() ?? "";

                    var analysisLink = $"{frontendUrl}/view-quote?{query}";

                    var emailToSend = new EmailTemplate
                    {
                        TemplateId = ProjectAnalysisEmail.TemplateId,
                        TemplateName = ProjectAnalysisEmail.TemplateName,
                        Subject = ProjectAnalysisEmail.Subject?.Replace(
                            "{{job.ProjectName}}",
                            job.ProjectName
                        ),
                        Body = ProjectAnalysisEmail
                            .Body.Replace("{{UserName}}", user.FirstName + " " + user.LastName)
                            .Replace("{{job.ProjectName}}", job.ProjectName)
                            .Replace("{{AnalysisLink}}", analysisLink)
                            .Replace("{{Header}}", ProjectAnalysisEmail.HeaderHtml)
                            .Replace("{{Footer}}", ProjectAnalysisEmail.FooterHtml),
                        Description = ProjectAnalysisEmail.Description,
                        FromName = ProjectAnalysisEmail.FromName,
                        FromEmail = ProjectAnalysisEmail.FromEmail,
                        IsHtml = ProjectAnalysisEmail.IsHtml,
                        HeaderHtml = ProjectAnalysisEmail.HeaderHtml,
                        FooterHtml = ProjectAnalysisEmail.FooterHtml,
                        LogoUrl = ProjectAnalysisEmail.LogoUrl,
                        InlineCss = ProjectAnalysisEmail.InlineCss,
                        LanguageCode = ProjectAnalysisEmail.LanguageCode,
                        IsActive = ProjectAnalysisEmail.IsActive,
                        VersionNumber = ProjectAnalysisEmail.VersionNumber,
                        CreatedBy = ProjectAnalysisEmail.CreatedBy,
                        CreatedDate = ProjectAnalysisEmail.CreatedDate,
                        ModifiedBy = ProjectAnalysisEmail.ModifiedBy,
                        ModifiedDate = ProjectAnalysisEmail.ModifiedDate,
                    };
                    try
                    {
                        await _emailService.SendEmailAsync(emailToSend, user.Email);

                        if (!string.IsNullOrEmpty(connectionId))
                        {
                            await _hubContext
                                .Clients.Client(connectionId)
                                .SendAsync("AnalysisEmailSent", new { JobId = jobId });
                        }

                        await MarkEmailSentAsync(jobId);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send email for job {jobId}: {ex.Message}");
                    }
                }

                if (!string.IsNullOrEmpty(connectionId))
                {
                    try
                    {
                        await _hubContext
                            .Clients.Client(connectionId)
                            .SendAsync(
                                "JobProcessingComplete",
                                new
                                {
                                    JobId = jobId,
                                    Message = "Document processing complete. The analysis report is available.",
                                }
                            );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"SignalR JobProcessingComplete send failed for job {jobId}: {ex.Message}"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing documents for job {jobId}: {ex.Message}");
                var log = new DocumentProcessingLogModel()
                {
                    Location = "ProcessDocumentsForJobAsync",
                    DateCreated = DateTime.UtcNow,
                    Description = $"MESSAGE: {ex.Message} STACKTRACE: {ex.StackTrace}",
                };
                _context.DocumentProcessingLog.Add(log);

                var job = await _context.Jobs.FindAsync(jobId);
                if (job != null)
                {
                    job.Status = "FAILED";
                }

                var analysisState = await _context.JobAnalysisStates.FirstOrDefaultAsync(s =>
                    s.JobId == jobId
                );

                if (analysisState == null)
                {
                    analysisState = new JobAnalysisState { JobId = jobId };
                    _context.JobAnalysisStates.Add(analysisState);
                }

                analysisState.StatusMessage = "Analysis failed.";
                analysisState.IsComplete = false;
                analysisState.HasFailed = true;
                analysisState.ErrorMessage = ex.Message;
                analysisState.LastUpdated = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                if (!string.IsNullOrEmpty(connectionId))
                {
                    try
                    {
                        await _hubContext
                            .Clients.Client(connectionId)
                            .SendAsync(
                                "JobProcessingFailed",
                                new { JobId = jobId, Error = ex.Message }
                            );
                    }
                    catch (Exception signalrEx)
                    {
                        Console.WriteLine(
                            $"SignalR JobProcessingFailed send failed for job {jobId}: {signalrEx.Message}"
                        );
                    }
                }
                throw;
            }
        }

        [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public async Task ProcessSelectedAnalysisForJobAsync(
            int jobId,
            List<string> documentUrls,
            List<string> promptKeys,
            string connectionId,
            bool generateDetailsWithAi,
            string userContextText,
            string userContextFileUrl,
            string budgetLevel
        )
        {
            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                // Log error
                return;
            }
            using var userAnalysisLock = AcquireUserAnalysisLock(job.UserId);

            try
            {
                var request = new AnalysisRequestDto
                {
                    AnalysisType = Models.Enums.AnalysisType.Selected,
                    PromptKeys = promptKeys,
                    DocumentUrls = documentUrls,
                    JobId = jobId,
                    UserId = job.UserId,
                };

                request.UserContext = await GetUserContextAsString(
                    userContextText,
                    userContextFileUrl
                );

                string finalReport = await _aiAnalysisService.PerformSelectedAnalysisAsync(
                    job.UserId,
                    request,
                    generateDetailsWithAi,
                    budgetLevel,
                    null,
                    connectionId
                );
                await EnsureScopePromptResultsPersistedAsync(jobId, finalReport);

                var result = new DocumentProcessingResult
                {
                    JobId = jobId,
                    BomJson = JsonSerializer.Serialize(""),
                    MaterialsEstimateJson = JsonSerializer.Serialize(""),
                    FullResponse = finalReport,
                    CreatedAt = DateTime.UtcNow,
                };

                _context.DocumentProcessingResults.Add(result);
                await _jobSubtaskTimelineSync.ReplaceSubtasksFromReportAsync(jobId, finalReport);
                job.Status = "PROCESSED";
                await _context.SaveChangesAsync();

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == job.UserId);
                if (user == null)
                {
                    Console.WriteLine($"User with ID {job.UserId} not found. Cannot send email.");
                }
                if (user != null)
                {
                    var ProjectAnalysisEmail = await _emailTemplate.GetTemplateAsync(
                        "ProjectAnalysisReadyEmail"
                    );

                    var jobAddress = await _context
                        .JobAddresses.Where(j => j.JobId == job.Id)
                        .FirstOrDefaultAsync();
                    var jobDocument = await _context
                        .JobDocuments.Where(d => d.JobId == job.Id)
                        .ToListAsync();
                    var frontendUrl =
                        Environment.GetEnvironmentVariable("FRONTEND_URL")
                        ?? "http://localhost:4200";

                    var query = HttpUtility.ParseQueryString(string.Empty);
                    query["jobId"] = job.Id.ToString();
                    query["operatingArea"] = job.OperatingArea;
                    query["address"] = job.Address;
                    query["projectName"] = job.ProjectName;
                    query["jobType"] = job.JobType;
                    query["buildingSize"] = job.BuildingSize.ToString();
                    query["wallStructure"] = job.WallStructure;
                    query["wallInsulation"] = job.WallInsulation;
                    query["roofStructure"] = job.RoofStructure;
                    query["roofInsulation"] = job.RoofInsulation;
                    query["electricalSupply"] = job.ElectricalSupplyNeeds;
                    query["finishes"] = job.Finishes;
                    query["foundation"] = job.Foundation;
                    query["date"] = job.DesiredStartDate.ToString("MM/dd/yyyy");
                    query["documents"] = jobDocument.Any()
                        ? string.Join(",", jobDocument.Select(d => d.Id))
                        : "";
                    query["latitude"] = jobAddress?.Latitude?.ToString() ?? "";
                    query["longitude"] = jobAddress?.Longitude?.ToString() ?? "";

                    var analysisLink = $"{frontendUrl}/view-quote?{query}";

                    var emailToSend = new EmailTemplate
                    {
                        TemplateId = ProjectAnalysisEmail.TemplateId,
                        TemplateName = ProjectAnalysisEmail.TemplateName,
                        Subject = ProjectAnalysisEmail.Subject?.Replace(
                            "{{job.ProjectName}}",
                            job.ProjectName
                        ),
                        Body = ProjectAnalysisEmail
                            .Body.Replace("{{UserName}}", user.FirstName + " " + user.LastName)
                            .Replace("{{job.ProjectName}}", job.ProjectName)
                            .Replace("{{AnalysisLink}}", analysisLink)
                            .Replace("{{Header}}", ProjectAnalysisEmail.HeaderHtml)
                            .Replace("{{Footer}}", ProjectAnalysisEmail.FooterHtml),
                        Description = ProjectAnalysisEmail.Description,
                        FromName = ProjectAnalysisEmail.FromName,
                        FromEmail = ProjectAnalysisEmail.FromEmail,
                        IsHtml = ProjectAnalysisEmail.IsHtml,
                        HeaderHtml = ProjectAnalysisEmail.HeaderHtml,
                        FooterHtml = ProjectAnalysisEmail.FooterHtml,
                        LogoUrl = ProjectAnalysisEmail.LogoUrl,
                        InlineCss = ProjectAnalysisEmail.InlineCss,
                        LanguageCode = ProjectAnalysisEmail.LanguageCode,
                        IsActive = ProjectAnalysisEmail.IsActive,
                        VersionNumber = ProjectAnalysisEmail.VersionNumber,
                        CreatedBy = ProjectAnalysisEmail.CreatedBy,
                        CreatedDate = ProjectAnalysisEmail.CreatedDate,
                        ModifiedBy = ProjectAnalysisEmail.ModifiedBy,
                        ModifiedDate = ProjectAnalysisEmail.ModifiedDate,
                    };

                    try
                    {
                        await _emailService.SendEmailAsync(emailToSend, user.Email);

                        await MarkEmailSentAsync(jobId);

                        if (!string.IsNullOrEmpty(connectionId))
                        {
                            await _hubContext
                                .Clients.Client(connectionId)
                                .SendAsync("AnalysisEmailSent", new { JobId = jobId });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send email for job {jobId}: {ex.Message}");
                    }
                }

                if (!string.IsNullOrEmpty(connectionId))
                {
                    try
                    {
                        await _hubContext
                            .Clients.Client(connectionId)
                            .SendAsync("AnalysisComplete", jobId, "Selected analysis is complete.");
                    }
                    catch (Exception signalrEx)
                    {
                        Console.WriteLine(
                            $"SignalR AnalysisComplete send failed for selected analysis job {jobId}: {signalrEx.Message}"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error
                job.Status = "FAILED";
                await _context.SaveChangesAsync();
                if (!string.IsNullOrEmpty(connectionId))
                {
                    try
                    {
                        await _hubContext
                            .Clients.Client(connectionId)
                            .SendAsync("AnalysisFailed", jobId, "Selected analysis failed.");
                    }
                    catch (Exception signalrEx)
                    {
                        Console.WriteLine(
                            $"SignalR AnalysisFailed send failed for selected analysis job {jobId}: {signalrEx.Message}"
                        );
                    }
                }
            }
        }

        private async Task<string> GetUserContextAsString(
            string userContextText,
            string userContextFileUrl
        )
        {
            var contextBuilder = new System.Text.StringBuilder();
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
                    //_logger.LogError(ex, "Failed to read user context file from URL: {Url}", userContextFileUrl);
                }
            }

            return contextBuilder.ToString();
        }

        [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public async Task ProcessRenovationAnalysisForJobAsync(
            int jobId,
            List<string> documentUrls,
            string connectionId,
            bool generateDetailsWithAi,
            string userContextText,
            string userContextFileUrl,
            string budgetLevel
        )
        {
            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                // Log error
                return;
            }
            using var userAnalysisLock = AcquireUserAnalysisLock(job.UserId);

            try
            {
                string finalReport = await _aiAnalysisService.PerformRenovationAnalysisAsync(
                    job.UserId,
                    documentUrls,
                    job,
                    generateDetailsWithAi,
                    userContextText,
                    userContextFileUrl,
                    budgetLevel,
                    connectionId
                );
                await EnsureScopePromptResultsPersistedAsync(jobId, finalReport);

                var result = new DocumentProcessingResult
                {
                    JobId = jobId,
                    BomJson = JsonSerializer.Serialize(""),
                    MaterialsEstimateJson = JsonSerializer.Serialize(""),
                    FullResponse = finalReport,
                    CreatedAt = DateTime.UtcNow,
                };

                _context.DocumentProcessingResults.Add(result);
                await _jobSubtaskTimelineSync.ReplaceSubtasksFromReportAsync(jobId, finalReport);
                job.Status = "PROCESSED";
                await _context.SaveChangesAsync();

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == job.UserId);
                if (user == null)
                {
                    Console.WriteLine($"User with ID {job.UserId} not found. Cannot send email.");
                }
                if (user != null)
                {
                    var ProjectAnalysisEmail = await _emailTemplate.GetTemplateAsync(
                        "ProjectAnalysisReadyEmail"
                    );

                    var jobAddress = await _context
                        .JobAddresses.Where(j => j.JobId == job.Id)
                        .FirstOrDefaultAsync();
                    var jobDocuments = await _context
                        .JobDocuments.Where(d => d.JobId == job.Id)
                        .ToListAsync();
                    var frontendUrl =
                        Environment.GetEnvironmentVariable("FRONTEND_URL")
                        ?? "http://localhost:4200";

                    var query = HttpUtility.ParseQueryString(string.Empty);
                    query["jobId"] = job.Id.ToString();
                    query["operatingArea"] = job.OperatingArea;
                    query["address"] = job.Address;
                    query["projectName"] = job.ProjectName;
                    query["jobType"] = job.JobType;
                    query["buildingSize"] = job.BuildingSize.ToString();
                    query["wallStructure"] = job.WallStructure;
                    query["wallInsulation"] = job.WallInsulation;
                    query["roofStructure"] = job.RoofStructure;
                    query["roofInsulation"] = job.RoofInsulation;
                    query["electricalSupply"] = job.ElectricalSupplyNeeds;
                    query["finishes"] = job.Finishes;
                    query["foundation"] = job.Foundation;
                    query["date"] = job.DesiredStartDate.ToString("MM/dd/yyyy");
                    query["documents"] = jobDocuments.Any()
                        ? string.Join(",", jobDocuments.Select(d => d.Id))
                        : "";
                    query["latitude"] = jobAddress?.Latitude?.ToString() ?? "";
                    query["longitude"] = jobAddress?.Longitude?.ToString() ?? "";

                    var analysisLink = $"{frontendUrl}/view-quote?{query}";

                    var emailToSend = new EmailTemplate
                    {
                        TemplateId = ProjectAnalysisEmail.TemplateId,
                        TemplateName = ProjectAnalysisEmail.TemplateName,
                        Subject = ProjectAnalysisEmail.Subject?.Replace(
                            "{{job.ProjectName}}",
                            job.ProjectName
                        ),
                        Body = ProjectAnalysisEmail
                            .Body.Replace("{{UserName}}", user.FirstName + " " + user.LastName)
                            .Replace("{{job.ProjectName}}", job.ProjectName)
                            .Replace("{{AnalysisLink}}", analysisLink)
                            .Replace("{{Header}}", ProjectAnalysisEmail.HeaderHtml)
                            .Replace("{{Footer}}", ProjectAnalysisEmail.FooterHtml),
                        Description = ProjectAnalysisEmail.Description,
                        FromName = ProjectAnalysisEmail.FromName,
                        FromEmail = ProjectAnalysisEmail.FromEmail,
                        IsHtml = ProjectAnalysisEmail.IsHtml,
                        HeaderHtml = ProjectAnalysisEmail.HeaderHtml,
                        FooterHtml = ProjectAnalysisEmail.FooterHtml,
                        LogoUrl = ProjectAnalysisEmail.LogoUrl,
                        InlineCss = ProjectAnalysisEmail.InlineCss,
                        LanguageCode = ProjectAnalysisEmail.LanguageCode,
                        IsActive = ProjectAnalysisEmail.IsActive,
                        VersionNumber = ProjectAnalysisEmail.VersionNumber,
                        CreatedBy = ProjectAnalysisEmail.CreatedBy,
                        CreatedDate = ProjectAnalysisEmail.CreatedDate,
                        ModifiedBy = ProjectAnalysisEmail.ModifiedBy,
                        ModifiedDate = ProjectAnalysisEmail.ModifiedDate,
                    };

                    try
                    {
                        await _emailService.SendEmailAsync(emailToSend, user.Email);

                        await MarkEmailSentAsync(jobId);

                        if (!string.IsNullOrEmpty(connectionId))
                        {
                            await _hubContext
                                .Clients.Client(connectionId)
                                .SendAsync("AnalysisEmailSent", new { JobId = jobId });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send email for job {jobId}: {ex.Message}");
                    }
                }

                if (!string.IsNullOrEmpty(connectionId))
                {
                    try
                    {
                        await _hubContext
                            .Clients.Client(connectionId)
                            .SendAsync(
                                "AnalysisComplete",
                                jobId,
                                "Renovation analysis is complete."
                            );
                    }
                    catch (Exception signalrEx)
                    {
                        Console.WriteLine(
                            $"SignalR AnalysisComplete send failed for renovation analysis job {jobId}: {signalrEx.Message}"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error
                job.Status = "FAILED";
                await _context.SaveChangesAsync();
                if (!string.IsNullOrEmpty(connectionId))
                {
                    try
                    {
                        await _hubContext
                            .Clients.Client(connectionId)
                            .SendAsync("AnalysisFailed", jobId, "Renovation analysis failed.");
                    }
                    catch (Exception signalrEx)
                    {
                        Console.WriteLine(
                            $"SignalR AnalysisFailed send failed for renovation analysis job {jobId}: {signalrEx.Message}"
                        );
                    }
                }
            }
        }

        private async Task MarkEmailSentAsync(int jobId)
        {
            var emailSentAtIso = DateTime.UtcNow.ToString("O");
            var rows = await _context.Database.ExecuteSqlInterpolatedAsync(
                $@"
UPDATE [JobAnalysisStates]
SET [ExtractedDataJson] = JSON_MODIFY(
        JSON_MODIFY(
            COALESCE(NULLIF([ExtractedDataJson], ''), '{{}}'),
            '$.emailSent',
            CAST(1 AS bit)
        ),
        '$.emailSentAt',
        {emailSentAtIso}
    ),
    [LastUpdated] = SYSUTCDATETIME()
WHERE [JobId] = {jobId};
"
            );

            if (rows == 0)
            {
                // If state row doesn't exist yet, fall back to creating it and retry once.
                _context.JobAnalysisStates.Add(
                    new JobAnalysisState
                    {
                        JobId = jobId,
                        ExtractedDataJson = "{}",
                        LastUpdated = DateTime.UtcNow,
                    }
                );
                await _context.SaveChangesAsync();

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $@"
UPDATE [JobAnalysisStates]
SET [ExtractedDataJson] = JSON_MODIFY(
        JSON_MODIFY(
            COALESCE(NULLIF([ExtractedDataJson], ''), '{{}}'),
            '$.emailSent',
            CAST(1 AS bit)
        ),
        '$.emailSentAt',
        {emailSentAtIso}
    ),
    [LastUpdated] = SYSUTCDATETIME()
WHERE [JobId] = {jobId};
"
                );
            }
        }

        private IDisposable AcquireUserAnalysisLock(string? userId)
        {
            var key = string.IsNullOrWhiteSpace(userId) ? "unknown" : userId.Trim();
            return JobStorage
                .Current.GetConnection()
                .AcquireDistributedLock($"analysis:user:{key}", TimeSpan.FromHours(1));
        }

        /// <summary>
        /// Failsafe persistence path: if upstream parse/broadcast path is skipped,
        /// still persist scope-review JSON blocks from final report.
        /// </summary>
        private async Task EnsureScopePromptResultsPersistedAsync(int jobId, string? finalReport)
        {
            if (string.IsNullOrWhiteSpace(finalReport))
            {
                return;
            }

            var extracted = ExtractScopeJsonBySchema(finalReport);
            if (extracted.Count == 0)
            {
                return;
            }

            var existingPromptKeys = await _context.JobPromptResults
                .AsNoTracking()
                .Where(
                    r =>
                        r.JobId == jobId
                        && (
                            r.PromptKey == "executive-summary-prompt"
                            || r.PromptKey == "executive-summary-prompt.txt"
                            || r.PromptKey == "prompt-21-timeline"
                            || r.PromptKey == "prompt-21-timeline.txt"
                            || r.PromptKey == "prompt-25-cost-breakdowns"
                            || r.PromptKey == "prompt-25-cost-breakdowns.txt"
                        )
                )
                .Select(r => r.PromptKey)
                .ToListAsync();

            var existingSet = new HashSet<string>(
                existingPromptKeys.Select(CanonicalizeScopePromptKey),
                StringComparer.OrdinalIgnoreCase
            );
            foreach (var kvp in extracted)
            {
                var canonicalPromptKey = CanonicalizeScopePromptKey(kvp.Key);
                if (existingSet.Contains(canonicalPromptKey))
                {
                    continue;
                }

                _context.JobPromptResults.Add(
                    new JobPromptResult
                    {
                        JobId = jobId,
                        PromptKey = kvp.Key,
                        SchemaVersion = kvp.Value.SchemaVersion,
                        RawResponse = kvp.Value.Json,
                        ParsedJson = kvp.Value.Json,
                        CreatedAt = DateTime.UtcNow,
                    }
                );
                existingSet.Add(canonicalPromptKey);
            }
        }

        private sealed record ScopeJsonExtract(string SchemaVersion, string Json);

        private static string CanonicalizeScopePromptKey(string? promptKey)
        {
            var key = (promptKey ?? string.Empty).Trim();
            if (key.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                key = key[..^4];
            }
            return key;
        }

        private static Dictionary<string, ScopeJsonExtract> ExtractScopeJsonBySchema(string report)
        {
            var result = new Dictionary<string, ScopeJsonExtract>(StringComparer.OrdinalIgnoreCase);
            var blockRegex = new Regex(@"```json\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase);
            var matches = blockRegex.Matches(report);
            foreach (Match m in matches)
            {
                if (!m.Success || m.Groups.Count < 2)
                {
                    continue;
                }

                var json = m.Groups[1].Value?.Trim();
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("schemaVersion", out var schemaEl))
                    {
                        continue;
                    }

                    var schema = schemaEl.GetString() ?? string.Empty;
                    if (schema.Equals("executive_summary.v1", StringComparison.OrdinalIgnoreCase))
                    {
                        result["executive-summary-prompt"] = new ScopeJsonExtract(schema, json);
                    }
                    else if (schema.Equals("timeline.v1", StringComparison.OrdinalIgnoreCase))
                    {
                        result["prompt-21-timeline"] = new ScopeJsonExtract(schema, json);
                    }
                    else if (schema.Equals("cost_breakdowns.v1", StringComparison.OrdinalIgnoreCase))
                    {
                        result["prompt-25-cost-breakdowns"] = new ScopeJsonExtract(schema, json);
                    }
                }
                catch
                {
                    // Ignore invalid JSON blocks.
                }
            }

            return result;
        }
    }
}

