using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using ProbuildBackend.Models;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Middleware;
using ProbuildBackend.Interface;
using Microsoft.AspNetCore.Identity.UI.Services;
using ProbuildBackend.Models.DTO;


namespace ProbuildBackend.Services
{
    public class DocumentProcessorService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ProgressHub> _hubContext;
        private readonly IEmailSender _emailService;
        private readonly IComprehensiveAnalysisService _comprehensiveAnalysisService;

        public DocumentProcessorService(
            ApplicationDbContext context,
            IHubContext<ProgressHub> hubContext,
            IEmailSender emailService,
            IComprehensiveAnalysisService comprehensiveAnalysisService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _comprehensiveAnalysisService = comprehensiveAnalysisService ?? throw new ArgumentNullException(nameof(comprehensiveAnalysisService));
        }

        public async Task ProcessDocumentsForJobAsync(int jobId, List<string> documentUrls, string connectionId)
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

                string finalReport = await _comprehensiveAnalysisService.PerformAnalysisFromFilesAsync(job.UserId, documentUrls, job);

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
                    var subject = $"AI Processing Complete for Job {job.ProjectName}";
                    var body = $@"<h2>AI Processing Complete</h2>
                                  <p>The AI has finished processing the documents for your job '{job.ProjectName}'.</p>
                                  <p><strong>Job ID:</strong> {jobId}</p>
                                  <p>Check the application for the full analysis report.</p>";
                    try
                    {
                        await _emailService.SendEmailAsync(user.Email, subject, body);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send email for job {jobId}: {ex.Message}");
                    }
                }

                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync("JobProcessingComplete", new
                    {
                        JobId = jobId,
                        Message = "Document processing complete. The analysis report is available."
                    });
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
                    await _hubContext.Clients.Client(connectionId).SendAsync("JobProcessingFailed", new
                    {
                        JobId = jobId,
                        Error = ex.Message
                    });
                }
                throw;
            }
        }

    }
}