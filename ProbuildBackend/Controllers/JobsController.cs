using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Helpers;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;
using System.IO.Compression;
using System.Globalization;
using Hangfire;
using Microsoft.AspNetCore.Identity.UI.Services;
using BomWithCosts = ProbuildBackend.Models.BomWithCosts;
using ProbuildBackend.Interface;
using System.Linq;
using IEmailSender = ProbuildBackend.Interface.IEmailSender;
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
        private readonly IDocumentProcessorService _documentProcessorService;
        private readonly IEmailSender _emailService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly WebSocketManager _webSocketManager;

        public JobsController(
            ApplicationDbContext context,
            AzureBlobService azureBlobservice,
            IHubContext<ProgressHub> hubContext,
            IHttpContextAccessor httpContextAccessor,
            IDocumentProcessorService documentProcessorService,
            IEmailSender emailService,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            WebSocketManager webSocketManager
        )
        {
            _httpContextAccessor =
                httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _context = context;
            _azureBlobservice = azureBlobservice;
            _hubContext = hubContext;
            _documentProcessorService = documentProcessorService;
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _httpClientFactory =
                httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _webSocketManager = webSocketManager;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Models.JobModel>>> GetJobs()
        {
            return await _context.Jobs.ToListAsync();
        }

        [HttpGet("public")]
        public async Task<ActionResult<IEnumerable<JobDto>>> GetPublicJobs()
        {
            try
            {


            var jobs = await _context.Jobs
                .Where(j => j.BiddingType == "PUBLIC" && j.Status == "BIDDING")
                .Join(_context.JobAddresses,
                    j => j.Id,
                    a => a.JobId,
                    (j, a) => new { Job = j, Address = a })
                .ToListAsync();

            var jobDtos = new List<JobDto>();

            foreach (var item in jobs)
            {
                var subtasks = await _context.JobSubtasks
                    .Where(s => s.JobId == item.Job.Id && !s.Deleted)
                    .ToListAsync();

                var potentialStartDate = subtasks.Any() ? subtasks.Min(s => s.StartDate) : DateTime.MinValue;
                var potentialEndDate = subtasks.Any() ? subtasks.Max(s => s.EndDate) : DateTime.MinValue;
                var durationInDays = subtasks.Any() ? (potentialEndDate - potentialStartDate).Days : 0;
                var numberOfBids = await _context.Bids.CountAsync(b => b.JobId == item.Job.Id);
                var user = await _context.Users.FindAsync(item.Job.UserId);
                var ratings = await _context.Ratings.Where(r => r.RatedUserId == item.Job.UserId).ToListAsync();
                double clientRating = ratings.Any() ? ratings.Average(r => r.RatingValue) : 0;

                jobDtos.Add(new JobDto
                {
                    JobId = item.Job.Id,
                    ProjectName = item.Job.ProjectName,
                    JobType = item.Job.JobType,
                    Status = item.Job.Status,
                    Address = item.Address.FormattedAddress,
                    StreetNumber = item.Address.StreetNumber,
                    StreetName = item.Address.StreetName,
                    City = item.Address.City,
                    State = item.Address.State,
                    PostalCode = item.Address.PostalCode,
                    Country = item.Address.Country,
                    Latitude = item.Address.Latitude.ToString(),
                    Longitude = item.Address.Longitude.ToString(),
                    GooglePlaceId = item.Address.GooglePlaceId,
                    Trades = item.Job.RequiredSubcontractorTypes,
                    PotentialStartDate = potentialStartDate,
                    PotentialEndDate = potentialEndDate,
                    DurationInDays = durationInDays,
                    Subtasks = subtasks,
                    NumberOfBids = numberOfBids,
                    ClientName = $"{user.FirstName} {user.LastName}",
                    ClientCompanyName = user.CompanyName,
                    ClientRating = clientRating,
                    CreatedAt = item.Job.CreatedAt,
                    BiddingStartDate = item.Job.BiddingStartDate
                });
            }

            return Ok(jobDtos);
            }
            catch (Exception ex)
            {

                throw;
            }
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

                var user = await _context.Users.FindAsync(job.UserId);
                var ratings = await _context.Ratings.Where(r => r.RatedUserId == job.UserId).ToListAsync();
                double clientRating = ratings.Any() ? ratings.Average(r => r.RatingValue) : 0;

                var subtasks = await _context.JobSubtasks
                    .Where(s => s.JobId == job.Id && !s.Deleted)
                    .ToListAsync();

                var potentialStartDate = subtasks.Any() ? subtasks.Min(s => s.StartDate) : DateTime.MinValue;
                var potentialEndDate = subtasks.Any() ? subtasks.Max(s => s.EndDate) : DateTime.MinValue;
                var durationInDays = subtasks.Any() ? (potentialEndDate - potentialStartDate).Days : 0;

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
                    BiddingType = job.BiddingType,
                    OperatingArea = job.OperatingArea,
                    UserId = job.UserId,
                    Stories = job.Stories,
                    BuildingSize = job.BuildingSize,
                    Trades = job.RequiredSubcontractorTypes,
                    CreatedAt = job.CreatedAt,
                    BiddingStartDate = job.BiddingStartDate,
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
                    ClientName = user != null ? $"{user.FirstName} {user.LastName}" : "",
                    ClientCompanyName = user?.CompanyName,
                    ClientRating = clientRating,
                    PotentialStartDate = potentialStartDate,
                    PotentialEndDate = potentialEndDate,
                    DurationInDays = durationInDays,
                    Blueprint = null,
                    TemporaryFileUrls = null
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
                var document = await _context.JobDocuments.FirstOrDefaultAsync(
                    doc => doc.Id == documentId
                );

                if (document == null)
                {
                    return NotFound("Document not found.");
                }

                var (contentStream, contentType, originalFileName) =
                    await _azureBlobservice.GetBlobContentAsync(document.BlobUrl);

                if (contentType == "application/gzip")
                {
                    using var decompressedStream = new MemoryStream();
                    using (
                        var gzipStream = new GZipStream(contentStream, CompressionMode.Decompress)
                    )
                    {
                        await gzipStream.CopyToAsync(decompressedStream);
                    }
                    decompressedStream.Position = 0;
                    string decompressedContentType = FileHelpers.GetContentTypeFromFileName(originalFileName);
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

        [HttpPost("view")]
        public async Task<IActionResult> ViewDocument([FromBody] ViewDocumentRequest request)
        {
            try
            {
                var document = await _context.JobDocuments.FirstOrDefaultAsync(doc => doc.BlobUrl == request.DocumentUrl);

                if (document == null)
                {
                    return NotFound("Document not found.");
                }

                var sasUrl = _azureBlobservice.GenerateTemporaryPublicUrl(document.BlobUrl);

                if (string.IsNullOrEmpty(sasUrl))
                {
                    return StatusCode(500, "Could not generate viewable URL.");
                }

                return Ok(sasUrl);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

        [HttpGet("downloadFile")]
        public async Task<IActionResult> DownloadFile([FromQuery(Name = "fileUrl")] string fileUrl)
        {
            try
            {
                var (contentStream, contentType, originalFileName) =
                    await _azureBlobservice.GetBlobContentAsync(fileUrl);

                if (contentType == "application/gzip")
                {
                    var decompressedStream = new MemoryStream();
                    using (
                        var gzipStream = new GZipStream(contentStream, CompressionMode.Decompress)
                    )
                    {
                        await gzipStream.CopyToAsync(decompressedStream);
                    }

                    decompressedStream.Position = 0;

                    // ⛏ Infer the correct type from file extension (e.g., .pdf)
                    string inferredContentType = FileHelpers.GetContentTypeFromFileName(originalFileName);

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


        [HttpGet("documents/{id}")]
        public async Task<ActionResult<IEnumerable<object>>> GetJobDocuments(int id)
        {
            var documents = await _context.JobDocuments.Where(doc => doc.JobId == id).ToListAsync();

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
                    documentDetails.Add(
                        new { doc.Id, doc.JobId, doc.FileName, Size = properties.Content.Length }
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Error fetching properties for blob {doc.BlobUrl}: {ex.Message}"
                    );
                    documentDetails.Add(new { doc.Id, doc.JobId, doc.FileName, Size = 0L });
                }
            }

            return Ok(documentDetails);
        }

        [HttpGet("processing-results/{jobId}")]
        public async Task<ActionResult<IEnumerable<DocumentProcessingResult>>> GetProcessingResults(
            int jobId
        )
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
                return StatusCode(
                    500,
                    new { error = "Failed to fetch processing results", details = ex.Message }
                );
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
                    return Ok(
                        new
                        {
                            IsProcessingComplete = false,
                            Message = "AI is still processing the documents."
                        }
                    );
                }

                if (job.Status == "PROCESSED")
                {
                    return Ok(
                        new { IsProcessingComplete = true, Message = "AI processing is complete." }
                    );
                }
                else if (job.Status == "FAILED")
                {
                    return Ok(
                        new { IsProcessingComplete = false, Message = "AI processing failed." }
                    );
                }
                else
                {
                    return Ok(
                        new
                        {
                            IsProcessingComplete = false,
                            Message = "AI processing is incomplete or in an unexpected state."
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new { error = "Failed to check processing status", details = ex.Message }
                );
            }
        }


        [HttpPost]
        [RequestSizeLimit(200 * 1024 * 1024)]
        public async Task<IActionResult> PostJob([FromForm] JobDto jobRequest)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(
                async () =>
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
                            Status = jobRequest.Status,
                            BiddingType = "NOT_BIDDING",
                            CreatedAt = DateTime.UtcNow
                        };

                        if (jobRequest.UserContextFile != null)
                        {
                            var userContextFileUrl = (await _azureBlobservice.UploadFiles(new List<IFormFile> { jobRequest.UserContextFile }, null, null)).FirstOrDefault();
                        }

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
                            decimal lat = Math.Round(
                                Convert.ToDecimal(
                                    jobRequest.Latitude,
                                    CultureInfo.InvariantCulture
                                ),
                                6
                            );
                            decimal lon = Math.Round(
                                Convert.ToDecimal(
                                    jobRequest.Longitude,
                                    CultureInfo.InvariantCulture
                                ),
                                6
                            );
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
                                .Where(
                                    doc =>
                                        doc.SessionId == jobRequest.SessionId && doc.JobId == null
                                )
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

                            if (jobRequest.AnalysisType == "Comprehensive")
                            {
                                var userContextFileUrl = "";
                                if (jobRequest.UserContextFile != null)
                                {
                                    userContextFileUrl = (await _azureBlobservice.UploadFiles(new List<IFormFile> { jobRequest.UserContextFile }, null, null)).FirstOrDefault();
                                }
                                BackgroundJob.Enqueue(() => _documentProcessorService.ProcessDocumentsForJobAsync(job.Id, documentUrls, connectionId, jobRequest.GenerateDetailsWithAi, jobRequest.UserContextText, userContextFileUrl));
                            }
                            else if (jobRequest.AnalysisType == "Selected")
                            {
                                var userContextFileUrl = "";
                                if (jobRequest.UserContextFile != null)
                                {
                                    userContextFileUrl = (await _azureBlobservice.UploadFiles(new List<IFormFile> { jobRequest.UserContextFile }, null, null)).FirstOrDefault();
                                }
                                BackgroundJob.Enqueue(() => _documentProcessorService.ProcessSelectedAnalysisForJobAsync(job.Id, documentUrls, jobRequest.PromptKeys, connectionId, jobRequest.GenerateDetailsWithAi, jobRequest.UserContextText, userContextFileUrl));
                            }
                            else if (jobRequest.AnalysisType == "Renovation")
                            {
                                var userContextFileUrl = "";
                                if (jobRequest.UserContextFile != null)
                                {
                                    userContextFileUrl = (await _azureBlobservice.UploadFiles(new List<IFormFile> { jobRequest.UserContextFile }, null, null)).FirstOrDefault();
                                }
                                BackgroundJob.Enqueue(() => _documentProcessorService.ProcessRenovationAnalysisForJobAsync(job.Id, documentUrls, connectionId, jobRequest.GenerateDetailsWithAi, jobRequest.UserContextText, userContextFileUrl));
                            }
                        }

                        return Ok(jobRequest);
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return StatusCode(
                            500,
                            new { error = "Failed to create job", details = ex.Message }
                        );
                    }
                }
            );
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

                string connectionId =
                    jobRequest.connectionId
                    ?? _httpContextAccessor.HttpContext?.Connection.Id
                    ?? throw new InvalidOperationException("No valid connectionId provided.");

                Console.WriteLine($"Received connectionId from client: {connectionId}");

                uploadedFileUrls = await _azureBlobservice.UploadFiles(
                    jobRequest.Blueprint,
                    _hubContext,
                    connectionId
                );

                foreach (
                    var (file, url) in jobRequest.Blueprint.Zip(uploadedFileUrls, (f, u) => (f, u))
                )
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
                return StatusCode(
                    500,
                    new { error = "Failed to upload files", details = ex.Message }
                );
            }
        }
 
        [HttpPost("UploadQuote")]
        [RequestSizeLimit(200 * 1024 * 1024)]
        public async Task<IActionResult> UploadQuote([FromForm] UploadQuoteDto jobRequest)
        {
            if (jobRequest == null || jobRequest.Quote == null || !jobRequest.Quote.Any())
            {
                return BadRequest(new { error = "No quote file provided" });
            }

            var quoteFile = jobRequest.Quote.First();
            var allowedExtensions = new[] { ".pdf" };
            var extension = Path.GetExtension(quoteFile.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                return BadRequest(new { error = "Invalid file type. Only PDF files are allowed for quotes." });
            }

            var uploadedFileUrls = await _azureBlobservice.UploadFiles(
                jobRequest.Quote,
                _hubContext,
                jobRequest.connectionId
            );

            var fileUrl = uploadedFileUrls.FirstOrDefault();
            if (fileUrl != null)
            {
                var jobDocument = new JobDocumentModel
                {
                    JobId = null, // This will be associated with a bid, not a job directly
                    FileName = Path.GetFileName(new Uri(fileUrl).LocalPath),
                    BlobUrl = fileUrl,
                    SessionId = jobRequest.sessionId,
                    UploadedAt = DateTime.Now
                };
                _context.JobDocuments.Add(jobDocument);
                await _context.SaveChangesAsync();
            }

            var response = new UploadDocumentModel
            {
                Id = Guid.NewGuid().ToString(),
                Status = "Uploaded",
                FileUrls = uploadedFileUrls,
                FileNames = jobRequest.Quote.Select(f => f.FileName).ToList(),
                Message = $"Successfully uploaded {jobRequest.Quote.Count} quote(s)"
            };

            return Ok(response);
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
                if (isNew)
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
                    var message =
                        $"An item on the timeline for project {job.ProjectName} has been moved.";

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

                    await _webSocketManager.BroadcastMessageAsync(
                        notification.Message,
                        notification.Recipients
                    );
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
        public async Task<IActionResult> PutJob(int id, [FromBody] JobDto jobDto)
        {
            if (id != jobDto.JobId)
            {
                return BadRequest();
            }

            var existingJob = await _context.Jobs.FindAsync(id);
            if (existingJob == null)
            {
                return NotFound();
            }

            existingJob.ProjectName = jobDto.ProjectName;
            existingJob.JobType = jobDto.JobType;
            existingJob.Qty = jobDto.Qty;
            existingJob.DesiredStartDate = jobDto.DesiredStartDate;
            existingJob.WallStructure = jobDto.WallStructure;
            existingJob.WallStructureSubtask = jobDto.WallStructureSubtask;
            existingJob.WallInsulation = jobDto.WallInsulation;
            existingJob.WallInsulationSubtask = jobDto.WallInsulationSubtask;
            existingJob.RoofStructure = jobDto.RoofStructure;
            existingJob.RoofStructureSubtask = jobDto.RoofStructureSubtask;
            existingJob.RoofTypeSubtask = jobDto.RoofTypeSubtask;
            existingJob.RoofInsulation = jobDto.RoofInsulation;
            existingJob.Foundation = jobDto.Foundation;
            existingJob.FoundationSubtask = jobDto.FoundationSubtask;
            existingJob.Finishes = jobDto.Finishes;
            existingJob.FinishesSubtask = jobDto.FinishesSubtask;
            existingJob.ElectricalSupplyNeeds = jobDto.ElectricalSupplyNeeds;
            existingJob.ElectricalSupplyNeedsSubtask = jobDto.ElectricalSupplyNeedsSubtask;
            existingJob.Stories = jobDto.Stories;
            existingJob.BuildingSize = jobDto.BuildingSize;
            existingJob.OperatingArea = jobDto.OperatingArea;
            existingJob.Status = jobDto.Status;

            if (!string.IsNullOrEmpty(jobDto.BiddingType))
            {
                existingJob.BiddingType = jobDto.BiddingType;
                existingJob.RequiredSubcontractorTypes = jobDto.Trades;
                if (jobDto.BiddingType == "PUBLIC" || jobDto.BiddingType == "PRIVATE")
                {
                    existingJob.BiddingStartDate = DateTime.UtcNow;
                }
            }

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
        public async Task<IActionResult> UpdateJobAddress(
            int jobId,
            [FromBody] UpdateJobAddressDto addressDto
        )
        {
            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                return NotFound("Job not found.");
            }

            var address = await _context.JobAddresses.FirstOrDefaultAsync(a => a.JobId == jobId);
            if (address == null)
            {
                address = new AddressModel { JobId = jobId, CreatedAt = DateTime.UtcNow };
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
        public async Task<IActionResult> DeleteTemporaryFiles(
            [FromBody] DeleteTemporaryFilesRequest request
        )
        {
            await _azureBlobservice.DeleteTemporaryFiles(request.BlobUrls);
            return Ok();
        }

        [HttpGet("userId/{userId}")]
        public async Task<ActionResult<IEnumerable<Models.JobModel>>> GetJobsByUserId(string userId)
        {
            try
            {
                var jobs = await _context.Jobs
                    .Where(job => job.UserId == userId && job.Status != "ARCHIVED")
                    .ToListAsync();

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

        [HttpGet("assigned/{userId}")]
        public async Task<ActionResult<IEnumerable<JobModel>>> GetAssignedJobs(string userId)
        {
            var jobs = await _context.JobAssignments
                .Where(ja => ja.UserId == userId)
                .Join(_context.Jobs, ja => ja.JobId, j => j.Id, (ja, j) => j)
                .ToListAsync();

            return Ok(jobs);
        }





        [HttpPut("{jobId}/archive")]
        public async Task<IActionResult> ArchiveJob(int jobId)
        {
            var job = await _context.Jobs.FindAsync(jobId);

            if (job == null)
            {
                return NotFound();
            }

            var subtasks = await _context.JobSubtasks
                .Where(st => st.JobId == jobId && !st.Deleted)
                .ToListAsync();

            var completedCount = subtasks.Count(st => st.Status == "Completed");
            var totalCount = subtasks.Count;
            var progress =
                totalCount > 0 ? (int)Math.Round((double)completedCount / totalCount * 100) : 0;

            if (progress < 100)
            {
                return BadRequest("Job progress must be 100% to archive.");
            }

            job.Status = "ARCHIVED";
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("archived")]
        public async Task<ActionResult<IEnumerable<JobDto>>> GetArchivedJobs()
        {
            var jobs = await _context.Jobs
                .Where(j => j.Status == "ARCHIVED")
                .Select(
                    j =>
                        new JobDto
                        {
                            JobId = j.Id,
                            ProjectName = j.ProjectName,
                            JobType = j.JobType,
                            Status = j.Status,
                            // Note: CompletionDate is not in the JobModel, so it's omitted.
                        }
                )
                .ToListAsync();

            return Ok(jobs);
        }

        [HttpGet("dashboard")]
        public async Task<ActionResult<IEnumerable<JobDto>>> GetDashboardJobs()
        {
            var jobs = await _context.Jobs.Where(j => j.Status != "ARCHIVED").ToListAsync();

            var jobDtos = new List<JobDto>();

            foreach (var job in jobs)
            {
                var subtasks = await _context.JobSubtasks
                    .Where(st => st.JobId == job.Id && !st.Deleted)
                    .ToListAsync();

                var completedCount = subtasks.Count(st => st.Status == "Completed");
                var totalCount = subtasks.Count;
                var progress =
                    totalCount > 0 ? (int)Math.Round((double)completedCount / totalCount * 100) : 0;

                jobDtos.Add(
                    new JobDto
                    {
                        JobId = job.Id,
                        ProjectName = job.ProjectName,
                        JobType = job.JobType,
                        Status = job.Status,
                        Progress = progress,
                    }
                );
            }

            return Ok(jobDtos);
        }

        [HttpPut("{jobId}/status")]
        public async Task<IActionResult> UpdateJobStatus(int jobId, [FromBody] UpdateStatusRequest request)
        {
            var job = await _context.Jobs.FindAsync(jobId);

            if (job == null)
            {
                return NotFound();
            }

            job.Status = request.Status;
            await _context.SaveChangesAsync();

            return Ok();
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
                var apiKey =
                    Environment.GetEnvironmentVariable("MapsAPI")
                    ?? _config["GoogleMapsAPI:APIKey"];

                var url =
                    $"https://weather.googleapis.com/v1/forecast/days:lookup?key={apiKey}&location.latitude={latitude.ToString(CultureInfo.InvariantCulture)}&location.longitude={longitude.ToString(CultureInfo.InvariantCulture)}&unitsSystem=METRIC&days=10&pageSize=10";

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
        public async Task<IActionResult> NotifyTimelineUpdate(
            [FromBody] NotifyTimelineUpdateRequest request
        )
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
                        Message =
                            $"Task '{subtask.Task}' in job '{job.ProjectName}' has been updated.",
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


        [HttpGet("bidded/{userId}")]
        public async Task<ActionResult<IEnumerable<BidModel>>> GetBiddedJobs(string userId)
        {
            var bids = await _context.Bids
                .Where(b => b.UserId == userId)
                .Include(b => b.Job)
                .ToListAsync();

            return Ok(bids);
        }

        public class ViewDocumentRequest
        {
            public string DocumentUrl { get; set; }
        }

        public class DeleteTemporaryFilesRequest
        {
            public List<string> BlobUrls { get; set; }
        }

        public class UpdateStatusRequest
        {
            public string Status { get; set; }
        }
    }
}
