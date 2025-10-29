using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using System.Text.Json;
using System.Web;
using IEmailSender = ProbuildBackend.Interface.IEmailSender;
namespace ProbuildBackend.Services
{
    public class DocumentProcessorService : IDocumentProcessorService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ProgressHub> _hubContext;
        private readonly IEmailSender _emailService;
        private readonly IAiAnalysisService _aiAnalysisService;
        private readonly IConversationRepository _conversationRepository;
        private readonly AzureBlobService _azureBlobService;
        private readonly IBlueprintProcessingService _blueprintProcessingService;
        private readonly IEmailTemplateService _emailTemplate;
        public DocumentProcessorService(
            ApplicationDbContext context,
            IHubContext<ProgressHub> hubContext,
            IEmailSender emailService,
            IAiAnalysisService aiAnalysisService,
            IConversationRepository conversationRepository,
            AzureBlobService azureBlobService,
            IBlueprintProcessingService blueprintProcessingService,
            IEmailTemplateService emailTemplate
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
            _blueprintProcessingService = blueprintProcessingService;
            _emailTemplate = emailTemplate;
        }

        public async Task ProcessDocumentsForJobAsync(
            int jobId,
            List<string> documentUrls,
            string connectionId,
            bool generateDetailsWithAi,
            string userContextText,
            string userContextFileUrl
        )
        {
            try
            {
                var job = await _context.Jobs.FindAsync(jobId);
                if (job == null)
                {
                    throw new InvalidOperationException($"Job with ID {jobId} not found.");
                }

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
                    userContextFileUrl
                );

                await ProcessBlueprintAnalysisForJobAsync(jobId, documentUrls, connectionId);

                var processingResult = new DocumentProcessingResult
                {
                    JobId = jobId,
                    DocumentId = 0, // This might need re-evaluation if we need to link to a specific document.
                    BomJson = JsonSerializer.Serialize(""), // Placeholder, as analysis service handles this.
                    MaterialsEstimateJson = JsonSerializer.Serialize(""), // Placeholder.
                    FullResponse = finalReport,
                    CreatedAt = DateTime.UtcNow
                };

                _context.DocumentProcessingResults.Add(processingResult);
                await _context.SaveChangesAsync();

                if (user != null)
                {

                    var ProjectAnalysisEmail = await _emailTemplate.GetTemplateAsync("ProjectAnalysisReadyEmail");

                    var jobAddress = _context.JobAddresses.Where(j => j.JobId == job.Id).FirstOrDefault();

                    var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";

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
                    query["documents"] = string.Join(",", job.Documents.Select(d => d.Id));
                    query["latitude"] = jobAddress.Latitude.ToString();
                    query["longitude"] = jobAddress.Longitude.ToString();

                    var analysisLink = $"{frontendUrl}/view-quote?{query}";

                    ProjectAnalysisEmail.Subject = ProjectAnalysisEmail.Subject.Replace("{{job.ProjectName}}", job.ProjectName);

                    ProjectAnalysisEmail.Body = ProjectAnalysisEmail.Body.Replace("{{UserName}}", user.FirstName + " " + user.LastName)
                                                                             .Replace("{{job.ProjectName}}", job.ProjectName)
                                                                             .Replace("{{AnalysisLink}}", analysisLink).Replace("{{Header}}", ProjectAnalysisEmail.HeaderHtml)
                .Replace("{{Footer}}", ProjectAnalysisEmail.FooterHtml);
                    try
                    {
                        await _emailService.SendEmailAsync(ProjectAnalysisEmail, user.Email);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send email for job {jobId}: {ex.Message}");
                    }
                }

                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext.Clients
                        .Client(connectionId)
                        .SendAsync(
                            "JobProcessingComplete",
                            new
                            {
                                JobId = jobId,
                                Message = "Document processing complete. The analysis report is available."
                            }
                        );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing documents for job {jobId}: {ex.Message}");
                var log = new DocumentProcessingLogModel()
                {
                    Location = "ProcessDocumentsForJobAsync",
                    DateCreated = DateTime.UtcNow,
                    Description = $"MESSAGE: {ex.Message} STACKTRACE: {ex.StackTrace}"
                };
                _context.DocumentProcessingLog.Add(log);

                var job = await _context.Jobs.FindAsync(jobId);
                if (job != null)
                {
                    job.Status = "FAILED";
                }
                await _context.SaveChangesAsync();

                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext.Clients
                        .Client(connectionId)
                        .SendAsync(
                            "JobProcessingFailed",
                            new { JobId = jobId, Error = ex.Message }
                        );
                }
                throw;
            }
        }

        public async Task ProcessSelectedAnalysisForJobAsync(
            int jobId,
            List<string> documentUrls,
            List<string> promptKeys,
            string connectionId,
            bool generateDetailsWithAi,
            string userContextText,
            string userContextFileUrl
        )
        {
            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                // Log error
                return;
            }

            try
            {
                var request = new AnalysisRequestDto
                {
                    AnalysisType = Models.Enums.AnalysisType.Selected,
                    PromptKeys = promptKeys,
                    DocumentUrls = documentUrls,
                    JobId = jobId,
                    UserId = job.UserId
                };

                request.UserContext = await GetUserContextAsString(
                    userContextText,
                    userContextFileUrl
                );

                string finalReport = await _aiAnalysisService.PerformSelectedAnalysisAsync(
                    job.UserId,
                    request,
                    generateDetailsWithAi
                );

                await ProcessBlueprintAnalysisForJobAsync(jobId, documentUrls, connectionId);

                var result = new DocumentProcessingResult
                {
                    JobId = jobId,
                    BomJson = JsonSerializer.Serialize(""),
                    MaterialsEstimateJson = JsonSerializer.Serialize(""),
                    FullResponse = finalReport,
                    CreatedAt = DateTime.UtcNow
                };

                _context.DocumentProcessingResults.Add(result);
                job.Status = "PROCESSED";
                await _context.SaveChangesAsync();

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == job.UserId);
                if (user == null)
                {
                    Console.WriteLine($"User with ID {job.UserId} not found. Cannot send email.");
                }
                if (user != null)
                {
                    var ProjectAnalysisEmail = await _emailTemplate.GetTemplateAsync("ProjectAnalysisReadyEmail");

                    var jobAddress = _context.JobAddresses.Where(j => j.JobId == job.Id).FirstOrDefault();

                    var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";

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
                    query["documents"] = string.Join(",", job.Documents.Select(d => d.Id));
                    query["latitude"] = jobAddress.Latitude.ToString();
                    query["longitude"] = jobAddress.Longitude.ToString();

                    var analysisLink = $"{frontendUrl}/view-quote?{query}";

                    ProjectAnalysisEmail.Subject = ProjectAnalysisEmail.Subject.Replace("{{job.ProjectName}}", job.ProjectName);

                    ProjectAnalysisEmail.Body = ProjectAnalysisEmail.Body.Replace("{{UserName}}", user.FirstName + " " + user.LastName)
                                                                             .Replace("{{job.ProjectName}}", job.ProjectName)
                                                                             .Replace("{{AnalysisLink}}", analysisLink).Replace("{{Header}}", ProjectAnalysisEmail.HeaderHtml)
                .Replace("{{Footer}}", ProjectAnalysisEmail.FooterHtml);

                    try
                    {
                        await _emailService.SendEmailAsync(ProjectAnalysisEmail, user.Email);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send email for job {jobId}: {ex.Message}");
                    }
                }

                await _hubContext.Clients
                    .Client(connectionId)
                    .SendAsync("AnalysisComplete", jobId, "Selected analysis is complete.");
            }
            catch (Exception ex)
            {
                // Log error
                job.Status = "FAILED";
                await _context.SaveChangesAsync();
                await _hubContext.Clients
                    .Client(connectionId)
                    .SendAsync("AnalysisFailed", jobId, "Selected analysis failed.");
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

        public async Task ProcessRenovationAnalysisForJobAsync(
            int jobId,
            List<string> documentUrls,
            string connectionId,
            bool generateDetailsWithAi,
            string userContextText,
            string userContextFileUrl
        )
        {
            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                // Log error
                return;
            }

            try
            {
                string finalReport = await _aiAnalysisService.PerformRenovationAnalysisAsync(
                    job.UserId,
                    documentUrls,
                    job,
                    generateDetailsWithAi,
                    userContextText,
                    userContextFileUrl
                );

                var result = new DocumentProcessingResult
                {
                    JobId = jobId,
                    BomJson = JsonSerializer.Serialize(""),
                    MaterialsEstimateJson = JsonSerializer.Serialize(""),
                    FullResponse = finalReport,
                    CreatedAt = DateTime.UtcNow
                };

                _context.DocumentProcessingResults.Add(result);
                job.Status = "PROCESSED";
                await _context.SaveChangesAsync();

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == job.UserId);
                if (user == null)
                {
                    Console.WriteLine($"User with ID {job.UserId} not found. Cannot send email.");
                }
                if (user != null)
                {
                    var ProjectAnalysisEmail = await _emailTemplate.GetTemplateAsync("ProjectAnalysisReadyEmail");

                    var jobAddress = _context.JobAddresses.Where(j => j.JobId == job.Id).FirstOrDefault();

                    var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";

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
                    query["documents"] = string.Join(",", job.Documents.Select(d => d.Id));
                    query["latitude"] = jobAddress.Latitude.ToString();
                    query["longitude"] = jobAddress.Longitude.ToString();

                    var analysisLink = $"{frontendUrl}/view-quote?{query}";

                    ProjectAnalysisEmail.Subject = ProjectAnalysisEmail.Subject.Replace("{{job.ProjectName}}", job.ProjectName);

                    ProjectAnalysisEmail.Body = ProjectAnalysisEmail.Body.Replace("{{UserName}}", user.FirstName + " " + user.LastName)
                                                                             .Replace("{{job.ProjectName}}", job.ProjectName)
                                                                             .Replace("{{AnalysisLink}}", analysisLink).Replace("{{Header}}", ProjectAnalysisEmail.HeaderHtml)
                .Replace("{{Footer}}", ProjectAnalysisEmail.FooterHtml);

                    try
                    {
                        await _emailService.SendEmailAsync(ProjectAnalysisEmail, user.Email);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send email for job {jobId}: {ex.Message}");
                    }
                }

                await _hubContext.Clients
                    .Client(connectionId)
                    .SendAsync("AnalysisComplete", jobId, "Renovation analysis is complete.");
            }
            catch (Exception ex)
            {
                // Log error
                job.Status = "FAILED";
                await _context.SaveChangesAsync();
                await _hubContext.Clients
                    .Client(connectionId)
                    .SendAsync("AnalysisFailed", jobId, "Renovation analysis failed.");
            }
        }

        public async Task ProcessBlueprintAnalysisForJobAsync(int jobId, List<string> documentUrls, string connectionId)
        {
            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                await _hubContext.Clients.Client(connectionId).SendAsync("AnalysisFailed", jobId, "Job not found.");
                return;
            }

            try
            {
                foreach (var docUrl in documentUrls)
                {
                    await _blueprintProcessingService.ProcessBlueprintAsync(job.UserId, docUrl, jobId);
                }

                job.Status = "PROCESSED";
                await _context.SaveChangesAsync();

                await _hubContext.Clients.Client(connectionId).SendAsync("AnalysisComplete", jobId, "Blueprint analysis is complete.");
            }
            catch (Exception ex)
            {
                job.Status = "FAILED";
                await _context.SaveChangesAsync();
                await _hubContext.Clients.Client(connectionId).SendAsync("AnalysisFailed", jobId, "Blueprint analysis failed: " + ex.Message);
            }
        }
    }
}
