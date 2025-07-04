using System.Text.Json;
using System.Drawing;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using ProbuildBackend.Models;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Middleware;
using ProbuildBackend.Interface;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using Image = SixLabors.ImageSharp.Image;
using Point = SixLabors.ImageSharp.Point;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using Size = SixLabors.ImageSharp.Size;
using Microsoft.AspNetCore.Identity.UI.Services;
using ProbuildBackend.Models.DTO;


namespace ProbuildBackend.Services
{
    public class DocumentProcessorService
    {
        private readonly AzureBlobService _azureBlobService;

        private readonly Dictionary<string, decimal> _materialCosts;
        private readonly ApplicationDbContext _context; // Add database context
        private readonly IHubContext<ProgressHub> _hubContext; // Add SignalR hub context
        private readonly IEmailSender _emailService; // Add this
        public List<string> AIText = new List<string>();
        private readonly ITextExtractor _textExtractor;
        private readonly IAiService _aiService;

        public DocumentProcessorService(
            AzureBlobService azureBlobService,
            IConfiguration configuration,
            ApplicationDbContext context,
            IHubContext<ProgressHub> hubContext,
        IEmailSender emailService, IAiService aiService, ITextExtractor textExtractor)
        {
            _azureBlobService = azureBlobService ?? throw new ArgumentNullException(nameof(azureBlobService));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _aiService = aiService;

            _materialCosts = new Dictionary<string, decimal>
            {
                { "concrete", 100m },
                { "steel rebar", 0.8m },
                { "lumber", 2.5m }
            };

            _textExtractor = textExtractor;
        }

        public async Task<(BomWithCosts BomWithCosts, MaterialsEstimate MaterialsEstimate, List<string> FullResponse)> ProcessDocumentAsync(string blobUrl,JobModel job)
        {
            List<string> documentText = await ExtractTextFromBlob(blobUrl, job); // This is the full response
                                                                            // var bom = await GenerateBomFromText(documentText);
                                                                            //var bomWithCosts = CalculateCosts(bom);                                                                           //var materialsEstimate = await ExtractMaterialsEstimateFromText(documentText);
            return (null, null, documentText); // Return the full response
        }

        // New method to process multiple documents for a job
        public async Task ProcessDocumentsForJobAsync(int jobId, List<string> documentUrls, string connectionId)
        {
            try
            {
              List<string> FullResponseList = new List<string>();
                var bomResults = new List<BomWithCosts>();
                var materialsEstimates = new List<MaterialsEstimate>();

                // Retrieve the job to get the UserId
                var job = await _context.Jobs.FindAsync(jobId);
                if (job == null)
                {
                    throw new InvalidOperationException($"Job with ID {jobId} not found.");
                }

                // Retrieve the user's email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == job.UserId);
                if (user == null)
                {
                    Console.WriteLine($"User with ID {job.UserId} not found. Cannot send email.");
                }
                bool AIProcessed = false;
                // Process each document and save its results
                foreach (var url in documentUrls)
                {
                    // Find the document ID by BlobUrl
                    var document = await _context.JobDocuments
                        .FirstOrDefaultAsync(doc => doc.BlobUrl == url && doc.JobId == jobId);

                    if (document == null)
                    {
                        Console.WriteLine($"Document with BlobUrl {url} not found for JobId {jobId}. Skipping...");
                        continue;
                    }

                    // Process the document
                    var (bomWithCosts, materialsEstimate, fullResponse) = await ProcessDocumentAsync(url, job);

                    FullResponseList = fullResponse;
                    AIProcessed = true;

                    // Environment.Exit(0);
                    // Add to the lists for consolidation
                    bomResults.Add(bomWithCosts);
                    materialsEstimates.Add(materialsEstimate);
                }

                // Send an email notification
                if (AIProcessed)
                {
                    string RefineText = string.Empty;

                    foreach (var item in FullResponseList)
                    {
                        RefineText += " " + item;
                    }

                    string refinedText = await _aiService.RefineTextWithAiAsync(RefineText, "");

                    // Save the results to the DocumentProcessingResults table
                    var processingResult = new DocumentProcessingResult
                    {
                        JobId = jobId,
                        DocumentId = 0,
                        BomJson = JsonSerializer.Serialize(""),
                        MaterialsEstimateJson = JsonSerializer.Serialize(""),
                        FullResponse = refinedText, // Save the full response
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.DocumentProcessingResults.Add(processingResult);
                    await _context.SaveChangesAsync();

                    if (user != null)
                    {
                        var subject = $"AI Processing Complete for Job {jobId}";
                        var body = $@"<h2>AI Processing Complete</h2>
                              <p>The AI has finished processing a document for your job.</p>
                              <p><strong>Job ID:</strong> {jobId}</p>
                              <p><strong>Full Response Preview:</strong></p>
                              <p>Check the application for full details.</p>";

                        try
                        {
                            await _emailService.SendEmailAsync(user.Email, subject, body);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to send email: {ex.Message}");
                            // Log the error, but don't fail the entire job
                        }
                    }
                }
                // Consolidate the BOM results
                // var consolidatedBom = ConsolidateBomResults(bomResults);



                // Notify the user via SignalR
                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync("JobProcessingComplete", new
                    {
                        JobId = jobId,
                        // Bom = consolidatedBom,
                        Message = "Document processing complete. BOM generated."
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing documents for job {jobId}: {ex.Message}");

                //Log Error Occured
                var log = new DocumentProcessingLogModel()
                {
                    Location = "ProcessDocumentsForJobAsync",
                    DateCreated = DateTime.UtcNow,
                    Description = "MESSAGE: " + ex.Message + " STACKTRACE: " + ex.StackTrace
                };
                _context.DocumentProcessingLog.Add(log);
                await _context.SaveChangesAsync();

                // Update the job status to indicate failure
                var job = await _context.Jobs.FindAsync(jobId);
                if (job != null)
                {
                    job.Status = "FAILED";
                    await _context.SaveChangesAsync();
                }

                // Notify the user of the failure via SignalR
                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync("JobProcessingFailed", new
                    {
                        JobId = jobId,
                        Error = ex.Message
                    });
                }

                throw; // Re-throw for Hangfire retries
            }
        }

        // Helper method to consolidate BOM results from multiple documents
        private BomWithCosts ConsolidateBomResults(List<BomWithCosts> bomResults)
        {
            var consolidatedItems = new Dictionary<string, BomItemWithCost>();

            foreach (var bom in bomResults)
            {
                foreach (var item in bom.BillOfMaterials)
                {
                    string key = $"{item.Item}_{item.Unit}".ToLower();
                    if (consolidatedItems.ContainsKey(key))
                    {
                        // Merge quantities for the same item and unit
                        consolidatedItems[key].Quantity += item.Quantity;
                        consolidatedItems[key].TotalItemCost += item.TotalItemCost;
                    }
                    else
                    {
                        consolidatedItems[key] = new BomItemWithCost
                        {
                            Item = item.Item,
                            Quantity = item.Quantity,
                            Unit = item.Unit,
                            CostPerUnit = item.CostPerUnit,
                            TotalItemCost = item.TotalItemCost
                        };
                    }
                }
            }

            var consolidatedBom = new BomWithCosts
            {
                BillOfMaterials = consolidatedItems.Values.ToList(),
                TotalCost = consolidatedItems.Values.Sum(item => item.TotalItemCost)
            };

            return consolidatedBom;
        }

        public async Task<List<string>> ExtractTextFromBlob(string blobUrl, JobModel job)
        {
            try
            {
                var (contentStream, contentType, originalFileName) = await _azureBlobService.GetBlobContentAsync(blobUrl);

                if (contentType != "application/pdf")
                {
                    throw new InvalidOperationException($"Blob at {blobUrl} is not a PDF. Content-Type: {contentType}");
                }

                Console.WriteLine($"Extracting text from PDF at {blobUrl} using Gemini API.");
                string extractedText = await _textExtractor.ExtractTextWithOcr(blobUrl, contentStream, job);

                if (string.IsNullOrEmpty(extractedText))
                {
                    throw new InvalidOperationException($"No text could be extracted from PDF at {blobUrl}.");
                }
                AIText.Add(extractedText);
                Console.WriteLine($"Extracted {extractedText.Length} characters from PDF at {blobUrl}.");

                // Refine the extracted text using the Gemini API

                Console.WriteLine($"Document {AIText.Count} at {blobUrl} refined.");

                return AIText;
            }
            catch (Exception ex)
            {
                //Log Error Occured
                var log = new DocumentProcessingLogModel()
                {
                    Location = "ExtractTextFromBlob",
                    DateCreated = DateTime.UtcNow,
                    Description = "MESSAGE: " + ex.Message + " STACKTRACE: " + ex.StackTrace
                };
                _context.DocumentProcessingLog.Add(log);
                await _context.SaveChangesAsync();

                throw new InvalidOperationException($"Failed to extract text from blob: {blobUrl}", ex);
            }
        }




        // Helper class to render PDF content to ImageSharp image
        public class ImageRenderListener : IEventListener
        {
            private readonly Image<Rgba32> _image;

            public ImageRenderListener(Image<Rgba32> image)
            {
                _image = image;
            }

            public void EventOccurred(IEventData data, EventType type)
            {
                if (data is ImageRenderInfo imageData)
                {
                    var pdfImage = imageData.GetImage();
                    var imageBytes = pdfImage.GetImageBytes();
                    using var img = Image.Load<Rgba32>(imageBytes);

                    // Get image position and transformation
                    var matrix = imageData.GetImageCtm();
                    float x = matrix.Get(4); // Translation X
                    float y = matrix.Get(5); // Translation Y

                    // Adjust Y-coordinate for ImageSharp (PDF origin is bottom-left, ImageSharp is top-left)
                    _image.Mutate(ctx => ctx.DrawImage(img, new Point((int)x, _image.Height - (int)y - img.Height), 1.0f));
                }
            }

            public ICollection<EventType> GetSupportedEvents()
            {
                return new List<EventType> { EventType.RENDER_IMAGE };
            }
        }
    }
}