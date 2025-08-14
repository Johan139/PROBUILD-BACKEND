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
  public class DocumentProcessorService : IDocumentProcessorService
  {
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<ProgressHub> _hubContext;
    private readonly IEmailSender _emailService;
    private readonly IAiAnalysisService _aiAnalysisService;
    private readonly IConversationRepository _conversationRepository;
    private readonly AzureBlobService _azureBlobService;

    public DocumentProcessorService(
        ApplicationDbContext context,
        IHubContext<ProgressHub> hubContext,
        IEmailSender emailService,
        IAiAnalysisService aiAnalysisService,
        IConversationRepository conversationRepository,
        AzureBlobService azureBlobService)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _aiAnalysisService = aiAnalysisService ?? throw new ArgumentNullException(nameof(aiAnalysisService));
        _conversationRepository = conversationRepository ?? throw new ArgumentNullException(nameof(conversationRepository));
        _azureBlobService = azureBlobService;
    }

    public async Task ProcessDocumentsForJobAsync(int jobId, List<string> documentUrls, string connectionId, bool generateDetailsWithAi, string userContextText, string userContextFileUrl)
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

        string finalReport = await _aiAnalysisService.PerformComprehensiveAnalysisAsync(job.UserId, documentUrls, job, generateDetailsWithAi, userContextText, userContextFileUrl);

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

    public async Task ProcessSelectedAnalysisForJobAsync(int jobId, List<string> documentUrls, List<string> promptKeys, string connectionId, bool generateDetailsWithAi, string userContextText, string userContextFileUrl)
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

        request.UserContext = await GetUserContextAsString(userContextText, userContextFileUrl);
        var conversation = await _aiAnalysisService.PerformSelectedAnalysisAsync(request, generateDetailsWithAi);
        var messages = await _conversationRepository.GetMessagesAsync(conversation.Id);
        string finalReport = messages.LastOrDefault(m => m.Role == "model")?.Content ?? "";

        var result = new DocumentProcessingResult
        {
          JobId = jobId,
          FullResponse = finalReport,
          CreatedAt = DateTime.UtcNow
        };

        _context.DocumentProcessingResults.Add(result);
        job.Status = "PROCESSED";
        await _context.SaveChangesAsync();

        await _hubContext.Clients.Client(connectionId).SendAsync("AnalysisComplete", jobId, "Selected analysis is complete.");
      }
      catch (Exception ex)
      {
        // Log error
        job.Status = "FAILED";
        await _context.SaveChangesAsync();
        await _hubContext.Clients.Client(connectionId).SendAsync("AnalysisFailed", jobId, "Selected analysis failed.");
      }
    }
         private async Task<string> GetUserContextAsString(string userContextText, string userContextFileUrl)
        {
           var contextBuilder = new System.Text.StringBuilder();
           if (!string.IsNullOrWhiteSpace(userContextText))
           {
               contextBuilder.AppendLine("## User-Provided Context");
               contextBuilder.AppendLine(userContextText);
           }

           if (!string.IsNullOrWhiteSpace(userContextFileUrl))
           {
               try
               {
                   var (contentStream, _, _) = await _azureBlobService.GetBlobContentAsync(userContextFileUrl);
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
    }
}
