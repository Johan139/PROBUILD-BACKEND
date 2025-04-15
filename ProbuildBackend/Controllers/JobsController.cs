using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;
using System.Net;
using static Google.Apis.Requests.BatchRequest;
using System.IO.Compression;
using System.Globalization;
using Hangfire;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JobsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ProgressHub> _hubContext;
        private readonly AzureBlobService _azureBlobservice;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly DocumentProcessorService _documentProcessorService;

        public JobsController(
            ApplicationDbContext context,
            AzureBlobService azureBlobservice,
            IHubContext<ProgressHub> hubContext,
            IHttpContextAccessor httpContextAccessor,
            DocumentProcessorService documentProcessorService)
        {
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _context = context;
            _azureBlobservice = azureBlobservice;
            _hubContext = hubContext;
            _documentProcessorService = documentProcessorService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Models.JobModel>>> GetJobs()
        {
            return await _context.Jobs.ToListAsync();
        }

        [HttpGet("Id/{id}")]
        public async Task<ActionResult<Models.JobModel>> GetJob(int id)
        {
            var job = await _context.Jobs.FindAsync(id);

            if (job == null)
            {
                return NotFound();
            }

            return job;
        }

        [HttpGet("download/{documentId}")]
        public async Task<IActionResult> DownloadBlob(int documentId)
        {
            try
            {
                var document = await _context.JobDocuments
                    .FirstOrDefaultAsync(doc => doc.Id == documentId);

                if (document == null)
                {
                    return NotFound("Document not found.");
                }

                var (contentStream, contentType, originalFileName) = await _azureBlobservice.GetBlobContentAsync(document.BlobUrl);

                if (contentType == "application/gzip")
                {
                    using var decompressedStream = new MemoryStream();
                    using (var gzipStream = new GZipStream(contentStream, CompressionMode.Decompress))
                    {
                        await gzipStream.CopyToAsync(decompressedStream);
                    }
                    decompressedStream.Position = 0;
                    string decompressedContentType = GetContentTypeFromFileName(originalFileName);
                    return File(decompressedStream, decompressedContentType, originalFileName);
                }

                return File(contentStream, contentType, originalFileName);
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while fetching the blob: {ex.Message}");
            }
        }

        [HttpGet("documents/{id}")]
        public async Task<ActionResult<IEnumerable<object>>> GetJobDocuments(int id)
        {
            var documents = await _context.JobDocuments
                .Where(doc => doc.JobId == id)
                .ToListAsync();

            if (documents == null || !documents.Any())
            {
                return NotFound();
            }

            var documentDetails = new List<object>();
            foreach (var doc in documents)
            {
                try
                {
                    var properties = await _azureBlobservice.GetBlobContentAsync(doc.BlobUrl);
                    documentDetails.Add(new
                    {
                        doc.Id,
                        doc.JobId,
                        doc.FileName,
                        Size = properties.Content.Length
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching properties for blob {doc.BlobUrl}: {ex.Message}");
                    documentDetails.Add(new
                    {
                        doc.Id,
                        doc.JobId,
                        doc.FileName,
                        Size = 0L
                    });
                }
            }

            return Ok(documentDetails);
        }

        [HttpGet("processing-results/{jobId}")]
        public async Task<ActionResult<IEnumerable<DocumentProcessingResult>>> GetProcessingResults(int jobId)
        {
            try
            {
                var results = await _context.DocumentProcessingResults
                    .Where(r => r.JobId == jobId)
                    .Include(r => r.Document)
                    .ToListAsync();

                if (results == null || !results.Any())
                {
                    return StatusCode(500, new { error = "AI is still processing the document." });
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to fetch processing results", details = ex.Message });
            }
        }

        [HttpGet("processing-status/{jobId}")]
        public async Task<IActionResult> GetProcessingStatus(int jobId)
        {
            try
            {
                var job = await _context.Jobs.FindAsync(jobId);
                if (job == null)
                {
                    return NotFound($"Job with ID {jobId} not found.");
                }

                var documents = await _context.JobDocuments
                    .Where(doc => doc.JobId == jobId)
                    .ToListAsync();

                if (documents == null || !documents.Any())
                {
                    return NotFound($"No documents found for JobId {jobId}.");
                }

                var processingResults = await _context.DocumentProcessingResults
                    .Where(r => r.JobId == jobId)
                    .ToListAsync();

                bool isProcessingComplete = documents.Count == processingResults.Count;

                if (!isProcessingComplete)
                {
                    return Ok(new { IsProcessingComplete = false, Message = "AI is still processing the documents." });
                }

                if (job.Status == "PROCESSED")
                {
                    return Ok(new { IsProcessingComplete = true, Message = "AI processing is complete." });
                }
                else if (job.Status == "FAILED")
                {
                    return Ok(new { IsProcessingComplete = false, Message = "AI processing failed." });
                }
                else
                {
                    return Ok(new { IsProcessingComplete = false, Message = "AI processing is incomplete or in an unexpected state." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to check processing status", details = ex.Message });
            }
        }

        private string GetContentTypeFromFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                _ => "application/octet-stream"
            };
        }

        [HttpPost]
        [RequestSizeLimit(200 * 1024 * 1024)]
        public async Task<IActionResult> PostJob([FromForm] JobDto jobRequest)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var job = new JobModel
                    {
                        ProjectName = jobRequest.ProjectName,
                        JobType = jobRequest.JobType,
                        Qty = jobRequest.Qty,
                        DesiredStartDate = jobRequest.DesiredStartDate,
                        WallStructure = jobRequest.WallStructure,
                        WallStructureSubtask = jobRequest.WallStructureSubtask,
                        WallInsulation = jobRequest.WallInsulation,
                        WallInsulationSubtask = jobRequest.WallInsulationSubtask,
                        RoofStructure = jobRequest.RoofStructure,
                        RoofStructureSubtask = jobRequest.RoofStructureSubtask,
                        RoofTypeSubtask = jobRequest.RoofTypeSubtask,
                        RoofInsulation = jobRequest.RoofInsulation,
                        Foundation = jobRequest.Foundation,
                        FoundationSubtask = jobRequest.FoundationSubtask,
                        Finishes = jobRequest.Finishes,
                        FinishesSubtask = jobRequest.FinishesSubtask,
                        ElectricalSupplyNeeds = jobRequest.ElectricalSupplyNeeds,
                        ElectricalSupplyNeedsSubtask = jobRequest.ElectricalSupplyNeedsSubtask,
                        Stories = jobRequest.Stories,
                        BuildingSize = jobRequest.BuildingSize,
                        OperatingArea = jobRequest.OperatingArea,
                        UserId = jobRequest.UserId,
                        Status = jobRequest.Status
                    };

                    _context.Jobs.Add(job);
                    await _context.SaveChangesAsync();

                    if (!string.IsNullOrEmpty(jobRequest.Address))
                    {
                        decimal lat = Convert.ToDecimal(jobRequest.Latitude, CultureInfo.InvariantCulture);
                        decimal lon = Convert.ToDecimal(jobRequest.Longitude, CultureInfo.InvariantCulture);
                        var address = new AddressModel
                        {
                            FormattedAddress = jobRequest.Address,
                            StreetNumber = jobRequest.StreetNumber,
                            StreetName = jobRequest.StreetName,
                            City = jobRequest.City,
                            State = jobRequest.State,
                            PostalCode = jobRequest.PostalCode,
                            Country = jobRequest.Country,
                            Latitude = lat,
                            Longitude = lon,
                            GooglePlaceId = jobRequest.GooglePlaceId,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                            JobId = job.Id
                        };

                        _context.JobAddresses.Add(address);
                        await _context.SaveChangesAsync();
                    }

                    List<string> documentUrls = new List<string>();
                    if (!string.IsNullOrEmpty(jobRequest.SessionId))
                    {
                        var documents = await _context.JobDocuments
                            .Where(doc => doc.SessionId == jobRequest.SessionId && doc.JobId == null)
                            .ToListAsync();

                        foreach (var doc in documents)
                        {
                            doc.JobId = job.Id;
                            documentUrls.Add(doc.BlobUrl);
                        }
                        await _context.SaveChangesAsync();
                    }

                    if (documentUrls.Any())
                    {
                        string connectionId = _httpContextAccessor.HttpContext?.Connection.Id ?? string.Empty;
                        BackgroundJob.Enqueue(() => _documentProcessorService.ProcessDocumentsForJobAsync(job.Id, documentUrls, connectionId));
                    }

                    await transaction.CommitAsync();

                    return Ok(job);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, new { error = "Failed to create job", details = ex.Message });
                }
            });
        }

        [HttpPost("UploadImage")]
        [RequestSizeLimit(200 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage([FromForm] UploadDocumentDTO jobRequest)
        {
            try
            {
                if (jobRequest == null)
                {
                    return BadRequest(new { error = "Invalid job request" });
                }

                if (jobRequest.Blueprint == null || !jobRequest.Blueprint.Any())
                {
                    return BadRequest(new { error = "No blueprint files provided" });
                }

                var allowedExtensions = new[] { ".pdf", ".png", ".jpg", ".jpeg" };
                var uploadedFileUrls = new List<string>();

                foreach (var file in jobRequest.Blueprint)
                {
                    if (file.Length == 0)
                    {
                        return BadRequest(new { error = $"Empty file detected: {file.FileName}" });
                    }

                    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (!allowedExtensions.Contains(extension))
                    {
                        return BadRequest(new { error = $"Invalid file type: {file.FileName}" });
                    }
                }

                string connectionId = jobRequest.connectionId ?? _httpContextAccessor.HttpContext?.Connection.Id
                    ?? throw new InvalidOperationException("No valid connectionId provided.");

                Console.WriteLine($"Received connectionId from client: {connectionId}");

                uploadedFileUrls = await _azureBlobservice.UploadFiles(jobRequest.Blueprint, _hubContext, connectionId);

                foreach (var (file, url) in jobRequest.Blueprint.Zip(uploadedFileUrls, (f, u) => (f, u)))
                {
                    string blobFileName = Path.GetFileName(new Uri(url).LocalPath);

                    Console.WriteLine($"Original file.FileName: {file.FileName}");
                    Console.WriteLine($"Blob URL from Azure: {url}");
                    Console.WriteLine($"Extracted Blob FileName: {blobFileName}");

                    var jobDocument = new JobDocumentModel
                    {
                        JobId = null,
                        FileName = blobFileName,
                        BlobUrl = url,
                        SessionId = jobRequest.sessionId,
                        UploadedAt = DateTime.Now
                    };
                    _context.JobDocuments.Add(jobDocument);
                }
                await _context.SaveChangesAsync();

                var bomResults = new List<BomWithCosts>();

                var response = new UploadDocumentModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Status = "Uploaded",
                    FileUrls = uploadedFileUrls,
                    FileNames = jobRequest.Blueprint.Select(f => f.FileName).ToList(),
                    Message = $"Successfully uploaded {jobRequest.Blueprint.Count} file(s)",
                    BillOfMaterials = bomResults
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to upload files", details = ex.Message });
            }
        }

        public async Task ProcessDocumentAndGenerateBomAsync(string blobUrl, string connectionId)
        {
            try
            {
                string documentText = await _documentProcessorService.ExtractTextFromBlob(blobUrl);
                var bom = await _documentProcessorService.GenerateBomFromText(documentText);
                var bomWithCosts = _documentProcessorService.CalculateCosts(bom);

                await _hubContext.Clients.Client(connectionId).SendAsync("BomGenerated", new
                {
                    BlobUrl = blobUrl,
                    Bom = bomWithCosts
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing document {blobUrl}: {ex.Message}");
                await _hubContext.Clients.Client(connectionId).SendAsync("BomGenerationFailed", new
                {
                    BlobUrl = blobUrl,
                    Error = ex.Message
                });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutJob(int id, [FromBody] JobModel job)
        {
            Console.WriteLine(job.Id);
            if (id != job.Id)
            {
                return BadRequest();
            }

            _context.Entry(job).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!JobExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        [HttpPost("DeleteTemporaryFiles")]
        public async Task<IActionResult> DeleteTemporaryFiles([FromBody] DeleteTemporaryFilesRequest request)
        {
            await _azureBlobservice.DeleteTemporaryFiles(request.BlobUrls);
            return Ok();
        }

        [HttpGet("userId/{userId}")]
        public async Task<ActionResult<IEnumerable<Models.JobModel>>> GetJobsByUserId(string userId)
        {
            try
            {
                var jobs = await _context.Jobs.Where(job => job.UserId == userId).ToListAsync();

                if (jobs == null || !jobs.Any())
                {
                    return NotFound();
                }

                return Ok(jobs);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteJob(int id)
        {
            var job = await _context.Jobs.FindAsync(id);
            if (job == null)
            {
                return NotFound();
            }

            _context.Jobs.Remove(job);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool JobExists(int id)
        {
            return _context.Jobs.Any(e => e.Id == id);
        }
    }

    public class DeleteTemporaryFilesRequest
    {
        public List<string> BlobUrls { get; set; }
    }
}