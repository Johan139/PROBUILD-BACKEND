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
using Org.BouncyCastle.Asn1.X509;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Elastic.Apm.Api;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using static iText.StyledXmlParser.Jsoup.Select.Evaluator;
using static ProbuildBackend.Services.DocumentProcessorService;
using BomWithCosts = ProbuildBackend.Models.BomWithCosts;


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
        private readonly IEmailSender _emailService; // Add this
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly WebSocketManager _webSocketManager;

        public JobsController(
            ApplicationDbContext context,
            AzureBlobService azureBlobservice,
            IHubContext<ProgressHub> hubContext,
            IHttpContextAccessor httpContextAccessor,
            DocumentProcessorService documentProcessorService,
            IEmailSender emailService,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            WebSocketManager webSocketManager)
        {
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _context = context;
            _azureBlobservice = azureBlobservice;
            _hubContext = hubContext;
            _documentProcessorService = documentProcessorService;
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _webSocketManager = webSocketManager;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Models.JobModel>>> GetJobs()
        {
            return await _context.Jobs.ToListAsync();
        }

        [HttpGet("Id/{id}")]
        public async Task<ActionResult<JobDto>> GetJob(int id)
        {
            try
            {
                var job = await _context.Jobs.FindAsync(id);

                if (job == null)
                {
                    return NotFound();
                }

                var address = await _context.JobAddresses.FirstOrDefaultAsync(a => a.JobId == id);

                var jobDto = new JobDto
                {
                    JobId = job.Id,
                    ProjectName = job.ProjectName,
                    JobType = job.JobType,
                    Qty = job.Qty,
                    DesiredStartDate = job.DesiredStartDate,
                    WallStructure = job.WallStructure,
                    WallStructureSubtask = job.WallStructureSubtask,
                    WallInsulation = job.WallInsulation,
                    WallInsulationSubtask = job.WallInsulationSubtask,
                    RoofStructure = job.RoofStructure,
                    RoofStructureSubtask = job.RoofStructureSubtask,
                    RoofTypeSubtask = job.RoofTypeSubtask,
                    RoofInsulation = job.RoofInsulation,
                    RoofInsulationSubtask = job.RoofInsulationSubtask,
                    Foundation = job.Foundation,
                    FoundationSubtask = job.FoundationSubtask,
                    Finishes = job.Finishes,
                    FinishesSubtask = job.FinishesSubtask,
                    ElectricalSupplyNeeds = job.ElectricalSupplyNeeds,
                    ElectricalSupplyNeedsSubtask = job.ElectricalSupplyNeedsSubtask,
                    Status = job.Status,
                    OperatingArea = job.OperatingArea,
                    UserId = job.UserId,
                    //SessionId = job.SessionId,
                    Stories = job.Stories,
                    BuildingSize = job.BuildingSize,
                    // The following fields come from the address entity
                    Address = address?.FormattedAddress,
                    StreetNumber = address?.StreetNumber ?? "0",
                    StreetName = address?.StreetName ?? "",
                    City = address?.City ?? "",
                    State = address?.State ?? "",
                    PostalCode = address?.PostalCode ?? "",
                    Country = address?.Country ?? "",
                    Latitude = address?.Latitude.ToString(),
                    Longitude = address?.Longitude.ToString(),
                    GooglePlaceId = address?.GooglePlaceId ?? "",
                    // The following are optional form values if needed
                    Blueprint = null, // or populate if coming from elsewhere
                    TemporaryFileUrls = null // or populate if applicable
                };

                return Ok(jobDto);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "An error occurred while retrieving the job.");
            }
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
        [HttpGet("downloadFile")]
        public async Task<IActionResult> DownloadFile([FromQuery(Name = "fileUrl")] string fileUrl)
        {
            try
            {
                var (contentStream, contentType, originalFileName) = await _azureBlobservice.GetBlobContentAsync(fileUrl);

                if (contentType == "application/gzip")
                {
                    var decompressedStream = new MemoryStream();
                    using (var gzipStream = new GZipStream(contentStream, CompressionMode.Decompress))
                    {
                        await gzipStream.CopyToAsync(decompressedStream);
                    }

                    decompressedStream.Position = 0;

                    // ⛏ Infer the correct type from file extension (e.g., .pdf)
                    string inferredContentType = GetContentTypeFromFileName(originalFileName);

                    return File(decompressedStream, inferredContentType, originalFileName);
                }

                // ✅ if not gzip, just return with actual type
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

        [HttpGet("downloadNote/{documentId}")]
        public async Task<IActionResult> DownloadNoteBlob(int documentId)
        {
            try
            {
                var document = await _context.SubtaskNoteDocument
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
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            return extension switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".doc" => "application/msword",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xls" => "application/vnd.ms-excel",
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

                    var clientModel = new ClientDetailsModel()
                    {
                        FirstName = jobRequest.FirstName,
                        LastName = jobRequest.LastName,
                        Email = jobRequest.Email,
                        CompanyName = jobRequest.CompanyName,
                        Phone = jobRequest.Phone,
                        Position = jobRequest.Position,
                        CreatedAt = DateTime.Now,
                        JobId = job.Id
                    };

                    _context.ClientDetails.Add(clientModel);
                   await _context.SaveChangesAsync();

                    if (!string.IsNullOrEmpty(jobRequest.Address))
                    {
                        decimal lat = Math.Round(Convert.ToDecimal(jobRequest.Latitude, CultureInfo.InvariantCulture), 6);
                        decimal lon = Math.Round(Convert.ToDecimal(jobRequest.Longitude, CultureInfo.InvariantCulture), 6);
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

                    await transaction.CommitAsync();

                    if (documentUrls.Any())
                    {
                        string connectionId = _httpContextAccessor.HttpContext?.Connection.Id ?? string.Empty;
                        BackgroundJob.Enqueue(() => _documentProcessorService.ProcessDocumentsForJobAsync(job.Id, documentUrls, connectionId));
                    }
                    return Ok(jobRequest);
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

        [HttpPost("UploadNoteImage")]
        [RequestSizeLimit(200 * 1024 * 1024)]
        public async Task<IActionResult> UploadNoteImage([FromForm] UploadDocumentDTO jobRequest)
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

                    var NoteDocument = new SubtaskNoteDocumentModel
                    {
                        NoteId = null,
                        FileName = blobFileName,
                        BlobUrl = url,
                        sessionId = jobRequest.sessionId,
                        UploadedAt = DateTime.Now
                    };
                    _context.SubtaskNoteDocument.Add(NoteDocument);
                }
                await _context.SaveChangesAsync();

                var response = new UploadDocumentModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Status = "Uploaded",
                    FileUrls = uploadedFileUrls,
                    FileNames = jobRequest.Blueprint.Select(f => f.FileName).ToList(),
                    Message = $"Successfully uploaded {jobRequest.Blueprint.Count} file(s)",
                    BillOfMaterials = null
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to upload files", details = ex.Message });
            }
        }

        [HttpPost("subtask")]
        public async Task<IActionResult> SaveSubtasks([FromBody] SaveSubtasksRequest subtasks)
        {
            try
            {
                bool isNew = true;
                int jobID = 0;

            foreach (var subtask in subtasks.Subtasks)
            {
                    jobID = subtask.JobId;
                if (subtask.Id > 0)
                {
                        isNew = false;
                    // UPDATE
                    var existing = await _context.JobSubtasks.FindAsync(subtask.Id);
                    if (existing != null)
                    {
                        existing.Task = subtask.Task;
                        existing.Days = subtask.Days;
                        existing.StartDate = subtask.StartDate;
                        existing.EndDate = subtask.EndDate;
                        existing.Status = subtask.Status;
                        existing.GroupTitle = subtask.GroupTitle;
                        existing.Deleted = subtask.Deleted;

                    }
                }
                else
                {
                        // INSERT

                    _context.JobSubtasks.Add(subtask);

                }
            }
            if(isNew)
                {
                    var acceptance = new JobsTermsAgreement()
                    {
                        UserId = subtasks.UserId,
                        DateAgreed = DateTime.UtcNow,
                        JobId = jobID
                    };
                    _context.JobsTermsAgreement.Add(acceptance);
                }

            await _context.SaveChangesAsync();

            // After saving subtasks, send notifications
            var job = await _context.Jobs.FindAsync(jobID);
            if (job != null)
            {
                var assignments = await _context.JobAssignments
                    .Where(a => a.JobId == jobID)
                    .ToListAsync();

                var userIds = assignments.Select(a => a.UserId).ToList();
                var message = $"An item on the timeline for project {job.ProjectName} has been moved.";

                var notification = new NotificationModel
                {
                    Message = message,
                    Recipients = userIds,
                    Timestamp = DateTime.UtcNow,
                    JobId = job.Id,
                    SenderId = subtasks.UserId
                };

                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();

                await _webSocketManager.BroadcastMessageAsync(notification.Message, notification.Recipients);
            }

            return Ok("Subtasks processed");
            }
            catch (Exception ex)
            {

                throw;
            }
        }
        [HttpGet("subtasks/{jobId}")]
        public async Task<IActionResult> GetSubtasksForJob(int jobId)
        {
            try
            {
            var subtasks = await _context.JobSubtasks
                .Where(st => st.JobId == jobId && st.Deleted == false)
                .ToListAsync();

            if (!subtasks.Any())
                    return Ok(new List<JobSubtasksModel>());

                return Ok(subtasks);
            }
            catch (Exception ex)
            {

                throw;
            }
        }

        [HttpPost("{id}")]
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

        [HttpPut("{jobId}/address")]
        public async Task<IActionResult> UpdateJobAddress(int jobId, [FromBody] UpdateJobAddressDto addressDto)
        {
            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                return NotFound("Job not found.");
            }

            var address = await _context.JobAddresses.FirstOrDefaultAsync(a => a.JobId == jobId);
            if (address == null)
            {
                address = new AddressModel
                {
                    JobId = jobId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.JobAddresses.Add(address);
            }
            address.StreetNumber = addressDto.StreetNumber;
            address.StreetName = addressDto.StreetName;
            address.City = addressDto.City;
            address.State = addressDto.State;
            address.PostalCode = addressDto.PostalCode;
            address.Country = addressDto.Country;
            address.Latitude = (decimal)addressDto.Latitude;
            address.Longitude = (decimal)addressDto.Longitude;
            address.FormattedAddress = addressDto.FormattedAddress;
            address.GooglePlaceId = addressDto.GooglePlaceId;
            address.UpdatedAt = DateTime.UtcNow;

            job.Address = addressDto.FormattedAddress;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!JobExists(jobId))
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

        [HttpGet("GetNotesByUserId/{userId}")]
        public async Task<ActionResult<IEnumerable<object>>> GetNotesByUserId(string userId)
        {
            try
            {
                var assignedNotes = await _context.SubtaskNoteUser
                    .Where(link => link.UserId == userId)
                    .Select(link => link.SubtaskNoteId)
                    .ToListAsync();

                if (!assignedNotes.Any())
                    return NotFound("No notes assigned to this user.");



                var notes = await (
     from note in _context.SubtaskNote
     join job in _context.Jobs on note.JobId equals job.Id
     where assignedNotes.Contains(note.Id)
     select new
     {
         note.Id,
         note.JobId,
         job.ProjectName,
         note.JobSubtaskId,
         note.NoteText,
         note.CreatedByUserId,
         note.CreatedAt,
         note.ModifiedAt
     }
 ).ToListAsync();

                var groupedNotes = (from note in notes
                                                join subtask in _context.JobSubtasks
                                                on note.JobSubtaskId equals subtask.Id
                                                group new { note, subtask } by new { note.JobId, note.JobSubtaskId } into g
                                                select new
                                                {
                                                    JobId = g.Key.JobId,
                                                    JobSubtaskId = g.Key.JobSubtaskId,
                                                    ProjectName = g.First().note.ProjectName,
                                                    CreatedAt = g.Min(x => x.note.CreatedAt),
                                                    SubtaskName = g.First().subtask.Task,
                                                    Notes = g.Select(x => new
                                                    {
                                                        x.note.Id,
                                                        x.note.NoteText,
                                                        x.note.CreatedByUserId,
                                                        x.note.CreatedAt,
                                                        x.note.ModifiedAt
                                                    }).ToList()
                                                }).ToList();

                return Ok(groupedNotes);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to fetch user-assigned notes", details = ex.Message });
            }
        }
        [HttpPost("UpdateNoteStatus")]
        public async Task<IActionResult> UpdateNoteStatus([FromForm] SubtaskNoteModel noteUpdate)
        {
            try
            {


            var note = await _context.SubtaskNote.Where(m => m.JobSubtaskId == noteUpdate.JobSubtaskId && (m.Approved != true && m.Rejected != true)).ToListAsync();
            if (note == null) return NotFound();
                foreach (var item in note)
                {
                    item.Approved = noteUpdate.Approved;
                    item.Rejected = noteUpdate.Rejected;
                    item.ModifiedAt = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();
                if ((bool)noteUpdate.Approved)
                {
                    var subtask = await _context.JobSubtasks.FindAsync(noteUpdate.JobSubtaskId);
                    subtask.Status = "Completed";

                    var noteResponse = new SubtaskNoteModel
                    {
                        JobId = noteUpdate.JobId,
                        JobSubtaskId = noteUpdate.JobSubtaskId,
                        NoteText = noteUpdate.NoteText,
                        CreatedByUserId = noteUpdate.CreatedByUserId,
                        Approved = true,
                        CreatedAt = DateTime.UtcNow,
                        ModifiedAt = DateTime.UtcNow
                    };
                    _context.SubtaskNote.Add(noteResponse);
                    await _context.SaveChangesAsync();
                    var usernote = new SubtaskNoteUserModel
                    {
                        SubtaskNoteId = noteResponse.Id,
                        UserId = noteUpdate.CreatedByUserId
                    };
                    _context.SubtaskNoteUser.Add(usernote);
                    await _context.SaveChangesAsync();

                }
                else
                {

                    var noteResponse = new SubtaskNoteModel
                    {
                        JobId = noteUpdate.JobId,
                        JobSubtaskId = noteUpdate.JobSubtaskId,
                        NoteText = noteUpdate.NoteText,
                        CreatedByUserId = noteUpdate.CreatedByUserId,
                        Approved = true,
                        CreatedAt = DateTime.UtcNow,
                        ModifiedAt = DateTime.UtcNow
                    };
                    _context.SubtaskNote.Add(noteResponse);
                    await _context.SaveChangesAsync();
                    var usernote = new SubtaskNoteUserModel
                    {
                        SubtaskNoteId = noteResponse.Id,
                        UserId = noteUpdate.CreatedByUserId
                    };
                    _context.SubtaskNoteUser.Add(usernote);
                    await _context.SaveChangesAsync();

                }

                    return Ok(note);
            }
            catch (Exception ex)
            {

                throw;
            }
        }

        [HttpGet("GetNoteDocuments/{noteId}")]
        public async Task<IActionResult> GetNoteDocuments(int noteId)
        {
            var documents = await _context.SubtaskNoteDocument
                .Where(doc => doc.SubTaskId == noteId)
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
                        doc.NoteId,
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
                        doc.NoteId,
                        doc.FileName,
                        Size = 0L
                    });
                }
            }

            return Ok(documentDetails);
        }

        [HttpPost("SaveSubtaskNote")]
        public async Task<IActionResult> SaveSubtaskNote([FromForm] SubtaskNoteDTO request)
        {

            List<string> useridEmail = new List<string>();
            if (string.IsNullOrWhiteSpace(request.NoteText) || string.IsNullOrWhiteSpace(request.CreatedByUserId))
                return BadRequest("Note text and user ID are required.");

            var note = new SubtaskNoteModel
            {
                JobId = request.JobId,
                JobSubtaskId = request.JobSubtaskId,
                NoteText = request.NoteText,
                CreatedByUserId = request.CreatedByUserId,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };

            _context.SubtaskNote.Add(note);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(request.SessionId))
            {
                var tempFiles = await _context.SubtaskNoteDocument
                    .Where(d => d.sessionId == request.SessionId && d.NoteId == null)
                    .ToListAsync();

                foreach (var file in tempFiles)
                {
                    file.NoteId = note.Id;
                    file.SubTaskId = note.JobSubtaskId;
                }

                await _context.SaveChangesAsync();
            }
            var Jobs = await _context.Jobs
            .Where(d => d.Id == note.JobId)
            .ToListAsync();
            foreach (var item in Jobs)
            {

            var usernote = new SubtaskNoteUserModel
                {
                    SubtaskNoteId = note.Id,
                    UserId = item.UserId
                };
                useridEmail.Add(item.UserId);
                _context.SubtaskNoteUser.Add(usernote);
            }

            await _context.SaveChangesAsync();
            foreach (var item in useridEmail)
            {
                var userEmail = await _context.Users
                .Where(d => d.Id == item)
                .ToListAsync();

                var subject = $"New task requires an action";
                var body = $@"<p>A note has been placed for a subtask which requires action. Please check dashboard.</p>";

                try
                {
                    await _emailService.SendEmailAsync(userEmail[0].Email, subject, body);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to send email: {ex.Message}");
                    // Log the error, but don't fail the entire job
                }
            }


            return Ok(new
            {
                message = "Note and any uploaded files saved successfully.",
                noteId = note.Id
            });
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

        [HttpGet("weather-forecast")]
        public async Task<IActionResult> GetWeatherForecast(string lat, string lon)
        {
            try
            {
                lat = lat.Replace(',', '.');
                decimal latitude = decimal.Parse(lat, CultureInfo.InvariantCulture);
                lon = lon.Replace(',', '.');
                decimal longitude = decimal.Parse(lon, CultureInfo.InvariantCulture);

                var client = _httpClientFactory.CreateClient();
                var apiKey = Environment.GetEnvironmentVariable("MapsAPI")
                    ?? _config["GoogleMapsAPI:APIKey"];

                var url = $"https://weather.googleapis.com/v1/forecast/days:lookup?key={apiKey}&location.latitude={latitude.ToString(CultureInfo.InvariantCulture)}&location.longitude={longitude.ToString(CultureInfo.InvariantCulture)}&unitsSystem=METRIC&days=10&pageSize=10";

                var response = await client.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                return Content(content, "application/json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather API Error: {ex.Message}");
                throw;
            }
        }

        [HttpPost("NotifyTimelineUpdate")]
        public async Task<IActionResult> NotifyTimelineUpdate([FromBody] NotifyTimelineUpdateRequest request)
        {
            if (request == null)
            {
                return BadRequest("Invalid request.");
            }

            var job = await _context.Jobs.FindAsync(request.JobId);
            if (job == null)
            {
                return NotFound($"Job with ID {request.JobId} not found.");
            }

            var subtask = await _context.JobSubtasks.FindAsync(request.SubtaskId);
            if (subtask == null)
            {
                return NotFound($"Subtask with ID {request.SubtaskId} not found.");
            }

            var assignments = await _context.JobAssignments
                .Where(a => a.JobId == request.JobId)
                .ToListAsync();

            if (assignments.Any())
            {
                var recipientIds = assignments.Select(a => a.UserId).ToList();

                if (recipientIds.Any())
                {
                    var notification = new NotificationModel
                    {
                        Message = $"Task '{subtask.Task}' in job '{job.ProjectName}' has been updated.",
                        JobId = job.Id,
                        UserId = null, // Set to null as Recipients is the source of truth
                        SenderId = request.SenderId,
                        Timestamp = DateTime.UtcNow,
                        Recipients = recipientIds
                    };

                    _context.Notifications.Add(notification);
                    await _context.SaveChangesAsync();
                }
            }

            return Ok();
        }
    }

    public class DeleteTemporaryFilesRequest
    {
        public List<string> BlobUrls { get; set; }
    }
}
