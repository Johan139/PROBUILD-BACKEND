using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using BuildigBackend.Helpers;
using BuildigBackend.Interface;
using BuildigBackend.Middleware;
using BuildigBackend.Models;
using BuildigBackend.Models.DTO;
using BuildigBackend.Services;
using IEmailSender = BuildigBackend.Interface.IEmailSender;

namespace BuildigBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotesController : ControllerBase
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
        public readonly IEmailTemplateService _emailTemplate;

        public NotesController(
            ApplicationDbContext context,
            AzureBlobService azureBlobservice,
            IHubContext<ProgressHub> hubContext,
            IHttpContextAccessor httpContextAccessor,
            IDocumentProcessorService documentProcessorService,
            IEmailSender emailService,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            WebSocketManager webSocketManager,
            IEmailTemplateService emailTemplate
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
            _emailTemplate = emailTemplate;
        }

        [HttpGet("downloadNote/{documentId}")]
        public async Task<IActionResult> DownloadNoteBlob(int documentId)
        {
            try
            {
                var document = await _context.SubtaskNoteDocument.FirstOrDefaultAsync(doc =>
                    doc.Id == documentId
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
                    string decompressedContentType = FileHelpers.GetContentTypeFromFileName(
                        originalFileName
                    );
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

                    var NoteDocument = new SubtaskNoteDocumentModel
                    {
                        NoteId = null,
                        FileName = blobFileName,
                        BlobUrl = url,
                        sessionId = jobRequest.sessionId,
                        UploadedAt = DateTime.Now,
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
                    BillOfMaterials = null,
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

        [HttpGet("GetNotesByUserId/{userId}")]
        public async Task<ActionResult<IEnumerable<object>>> GetNotesByUserId(string userId)
        {
            try
            {
                var rows = await (
                    from link in _context.SubtaskNoteUser
                    join note in _context.SubtaskNote on link.SubtaskNoteId equals note.Id
                    join job in _context.Jobs on note.JobId equals job.Id
                    join subtask in _context.JobSubtasks on note.JobSubtaskId equals subtask.Id
                    where link.UserId == userId && !note.Archived
                    select new
                    {
                        note.Id,
                        note.JobId,
                        job.ProjectName,
                        note.JobSubtaskId,
                        SubtaskName = subtask.Task,
                        note.NoteText,
                        note.CreatedByUserId,
                        note.CreatedAt,
                        note.ModifiedAt,
                        note.Approved,
                        note.Rejected,
                        note.Archived,
                    }
                ).AsNoTracking().ToListAsync();

                if (!rows.Any())
                {
                    return NotFound("No notes assigned to this user.");
                }

                var groupedNotes = rows
                    .GroupBy(r => new { r.JobId, r.JobSubtaskId })
                    .Select(g => new
                    {
                        JobId = g.Key.JobId,
                        JobSubtaskId = g.Key.JobSubtaskId,
                        ProjectName = g.First().ProjectName,
                        CreatedAt = g.Min(x => x.CreatedAt),
                        SubtaskName = g.First().SubtaskName,
                        Notes = g.Select(x => new
                            {
                                x.Id,
                                x.NoteText,
                                x.CreatedByUserId,
                                x.CreatedAt,
                                x.ModifiedAt,
                                x.Approved,
                                x.Rejected,
                                x.Archived,
                            })
                            .ToList(),
                    })
                    .ToList();

                return Ok(groupedNotes);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new { error = "Failed to fetch user-assigned notes", details = ex.Message }
                );
            }
        }

        [HttpGet("notes/archived/{userId}")]
        public async Task<ActionResult<IEnumerable<object>>> GetArchivedNotes(string userId)
        {
            try
            {
                var assignedNotes = await _context
                    .SubtaskNoteUser.Where(link => link.UserId == userId)
                    .Select(link => link.SubtaskNoteId)
                    .ToListAsync();

                if (!assignedNotes.Any())
                    return Ok(new List<object>());

                var notes = await (
                    from note in _context.SubtaskNote
                    join job in _context.Jobs on note.JobId equals job.Id
                    where assignedNotes.Contains(note.Id) && note.Archived
                    select new
                    {
                        note.Id,
                        note.JobId,
                        job.ProjectName,
                        note.JobSubtaskId,
                        note.NoteText,
                        note.CreatedByUserId,
                        note.CreatedAt,
                        note.ModifiedAt,
                        note.Approved,
                        note.Rejected,
                        note.Archived,
                    }
                ).ToListAsync();

                var groupedNotes = (
                    from note in notes
                    join subtask in _context.JobSubtasks on note.JobSubtaskId equals subtask.Id
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
                                x.note.ModifiedAt,
                                x.note.Approved,
                                x.note.Rejected,
                                x.note.Archived,
                            })
                            .ToList(),
                    }
                ).ToList();

                return Ok(groupedNotes);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new { error = "Failed to fetch archived notes", details = ex.Message }
                );
            }
        }

        [HttpGet("notes/assigned/{userId}")]
        public async Task<ActionResult<IEnumerable<object>>> GetNotesForAssignedJobs(string userId)
        {
            try
            {
                var assignedJobIds = await _context
                    .JobAssignments.Where(ja => ja.UserId == userId)
                    .Select(ja => ja.JobId)
                    .Distinct()
                    .ToListAsync();

                if (!assignedJobIds.Any())
                {
                    return Ok(new List<object>());
                }

                var notesWithDetails = await (
                    from note in _context.SubtaskNote
                    join job in _context.Jobs on note.JobId equals job.Id
                    join subtask in _context.JobSubtasks on note.JobSubtaskId equals subtask.Id
                    where assignedJobIds.Contains(note.JobId) && !note.Archived
                    select new
                    {
                        note.Id,
                        note.JobId,
                        job.ProjectName,
                        note.JobSubtaskId,
                        SubtaskName = subtask.Task,
                        note.NoteText,
                        note.CreatedByUserId,
                        note.CreatedAt,
                        note.ModifiedAt,
                        note.Approved,
                        note.Rejected,
                        note.Archived,
                    }
                ).ToListAsync();

                var groupedNotes = notesWithDetails
                    .GroupBy(n => new { n.JobId, n.SubtaskName })
                    .Select(g => new
                    {
                        JobId = g.Key.JobId,
                        SubtaskName = g.Key.SubtaskName,
                        ProjectName = g.First().ProjectName,
                        JobSubtaskId = g.First().JobSubtaskId,
                        CreatedAt = g.Min(x => x.CreatedAt),
                        Notes = g.Select(x => new
                            {
                                x.Id,
                                x.NoteText,
                                x.CreatedByUserId,
                                x.CreatedAt,
                                x.ModifiedAt,
                                x.Approved,
                                x.Rejected,
                                x.Archived,
                            })
                            .ToList(),
                    })
                    .ToList();

                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(groupedNotes));

                return Ok(groupedNotes);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new { error = "Failed to fetch notes for assigned jobs", details = ex.Message }
                );
            }
        }

        [HttpGet("notes/archived/assigned/{userId}")]
        public async Task<ActionResult<IEnumerable<object>>> GetArchivedNotesForAssignedJobs(
            string userId
        )
        {
            try
            {
                var assignedJobIds = await _context
                    .JobAssignments.Where(ja => ja.UserId == userId)
                    .Select(ja => ja.JobId)
                    .Distinct()
                    .ToListAsync();

                if (!assignedJobIds.Any())
                {
                    return Ok(new List<object>());
                }

                var notesWithDetails = await (
                    from note in _context.SubtaskNote
                    join job in _context.Jobs on note.JobId equals job.Id
                    join subtask in _context.JobSubtasks on note.JobSubtaskId equals subtask.Id
                    where assignedJobIds.Contains(note.JobId) && note.Archived
                    select new
                    {
                        note.Id,
                        note.JobId,
                        job.ProjectName,
                        note.JobSubtaskId,
                        SubtaskName = subtask.Task,
                        note.NoteText,
                        note.CreatedByUserId,
                        note.CreatedAt,
                        note.ModifiedAt,
                        note.Approved,
                        note.Rejected,
                        note.Archived,
                    }
                ).ToListAsync();

                var groupedNotes = notesWithDetails
                    .GroupBy(n => new { n.JobId, n.SubtaskName })
                    .Select(g => new
                    {
                        JobId = g.Key.JobId,
                        SubtaskName = g.Key.SubtaskName,
                        ProjectName = g.First().ProjectName,
                        JobSubtaskId = g.First().JobSubtaskId,
                        CreatedAt = g.Min(x => x.CreatedAt),
                        Notes = g.Select(x => new
                            {
                                x.Id,
                                x.NoteText,
                                x.CreatedByUserId,
                                x.CreatedAt,
                                x.ModifiedAt,
                                x.Approved,
                                x.Rejected,
                                x.Archived,
                            })
                            .ToList(),
                    })
                    .ToList();

                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(groupedNotes));

                return Ok(groupedNotes);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        error = "Failed to fetch archived notes for assigned jobs",
                        details = ex.Message,
                    }
                );
            }
        }

        [HttpPost("UpdateNoteStatus")]
        public async Task<IActionResult> UpdateNoteStatus([FromForm] SubtaskNoteModel noteUpdate)
        {
            try
            {
                var note = await _context
                    .SubtaskNote.Where(m =>
                        m.JobSubtaskId == noteUpdate.JobSubtaskId && (!m.Approved && !m.Rejected)
                    )
                    .ToListAsync();
                if (note == null)
                    return NotFound();
                foreach (var item in note)
                {
                    item.Approved = noteUpdate.Approved;
                    item.Rejected = noteUpdate.Rejected;
                    item.Archived = noteUpdate.Archived;
                    item.ModifiedAt = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();
                if (noteUpdate.Approved)
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
                        ModifiedAt = DateTime.UtcNow,
                    };
                    _context.SubtaskNote.Add(noteResponse);
                    await _context.SaveChangesAsync();
                    var usernote = new SubtaskNoteUserModel
                    {
                        SubtaskNoteId = noteResponse.Id,
                        UserId = noteUpdate.CreatedByUserId,
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
                        ModifiedAt = DateTime.UtcNow,
                    };
                    _context.SubtaskNote.Add(noteResponse);
                    await _context.SaveChangesAsync();
                    var usernote = new SubtaskNoteUserModel
                    {
                        SubtaskNoteId = noteResponse.Id,
                        UserId = noteUpdate.CreatedByUserId,
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

        [HttpPost("notes/{noteId}/archive")]
        public async Task<IActionResult> ArchiveNote(int noteId)
        {
            var noteToArchive = await _context.SubtaskNote.FindAsync(noteId);

            if (noteToArchive == null)
            {
                return NotFound("Note not found.");
            }

            if (!noteToArchive.Approved && !noteToArchive.Rejected)
            {
                return BadRequest("Only approved or rejected notes can be archived.");
            }

            var notesToUpdate = await _context
                .SubtaskNote.Where(n => n.JobSubtaskId == noteToArchive.JobSubtaskId)
                .ToListAsync();

            foreach (var note in notesToUpdate)
            {
                note.Archived = true;
                note.ModifiedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("GetNoteDocuments/{noteId}")]
        public async Task<IActionResult> GetNoteDocuments(int noteId)
        {
            var documents = await _context
                .SubtaskNoteDocument.Where(doc => doc.SubTaskId == noteId)
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
                    documentDetails.Add(
                        new
                        {
                            doc.Id,
                            doc.NoteId,
                            doc.FileName,
                            Size = properties.Content.Length,
                        }
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Error fetching properties for blob {doc.BlobUrl}: {ex.Message}"
                    );
                    documentDetails.Add(
                        new
                        {
                            doc.Id,
                            doc.NoteId,
                            doc.FileName,
                            Size = 0L,
                        }
                    );
                }
            }

            return Ok(documentDetails);
        }

        [HttpPost("SaveSubtaskNote")]
        public async Task<IActionResult> SaveSubtaskNote([FromForm] SubtaskNoteDTO request)
        {
            List<string> useridEmail = new List<string>();
            if (
                string.IsNullOrWhiteSpace(request.NoteText)
                || string.IsNullOrWhiteSpace(request.CreatedByUserId)
            )
                return BadRequest("Note text and user ID are required.");

            var note = new SubtaskNoteModel
            {
                JobId = request.JobId,
                JobSubtaskId = request.JobSubtaskId,
                NoteText = request.NoteText,
                CreatedByUserId = request.CreatedByUserId,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
            };

            _context.SubtaskNote.Add(note);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(request.SessionId))
            {
                var tempFiles = await _context
                    .SubtaskNoteDocument.Where(d =>
                        d.sessionId == request.SessionId && d.NoteId == null
                    )
                    .ToListAsync();

                foreach (var file in tempFiles)
                {
                    file.NoteId = note.Id;
                    file.SubTaskId = note.JobSubtaskId;
                }

                await _context.SaveChangesAsync();
            }
            var Jobs = await _context.Jobs.Where(d => d.Id == note.JobId).ToListAsync();
            foreach (var item in Jobs)
            {
                var usernote = new SubtaskNoteUserModel
                {
                    SubtaskNoteId = note.Id,
                    UserId = item.UserId,
                };
                useridEmail.Add(item.UserId);
                _context.SubtaskNoteUser.Add(usernote);
            }

            await _context.SaveChangesAsync();
            foreach (var item in useridEmail)
            {
                var userEmail = await _context.Users.Where(d => d.Id == item).ToListAsync();
                var ActionRequired = await _emailTemplate.GetTemplateAsync(
                    "TaskActionRequiredEmail"
                );
                var frontendUrl =
                    Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
                var callbackUrl = $"{frontendUrl}/login";
                ActionRequired.Subject = ActionRequired.Subject.Replace(
                    "{{job.ProjectName}}",
                    Jobs[0].ProjectName
                );

                ActionRequired.Body = ActionRequired
                    .Body.Replace(
                        "{{UserName}}",
                        userEmail[0].FirstName + " " + userEmail[0].LastName
                    )
                    .Replace("{{job.ProjectName}}", Jobs[0].ProjectName)
                    .Replace("{{Header}}", ActionRequired.HeaderHtml)
                    .Replace("{{Footer}}", ActionRequired.FooterHtml)
                    .Replace("{{TaskLink}}", callbackUrl);

                try
                {
                    await _emailService.SendEmailAsync(ActionRequired, userEmail[0].Email);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to send email: {ex.Message}");
                    // Log the error, but don't fail the entire job
                }
            }

            return Ok(
                new
                {
                    message = "Note and any uploaded files saved successfully.",
                    noteId = note.Id,
                }
            );
        }

        [HttpGet("marketplace")]
        public async Task<IActionResult> GetMarketplaceNotes(
            [FromQuery] string requesterUserId,
            [FromQuery] int? jobId,
            [FromQuery] int? tradePackageId
        )
        {
            if (string.IsNullOrWhiteSpace(requesterUserId))
            {
                return BadRequest(new { error = "requesterUserId is required." });
            }

            if (!jobId.HasValue && !tradePackageId.HasValue)
            {
                return BadRequest(new { error = "Either jobId or tradePackageId is required." });
            }

            var normalizedRequester = requesterUserId.Trim();
            var visibilitySet = await ResolveVisibleScopesAsync(
                normalizedRequester,
                jobId,
                tradePackageId
            );

            var query = _context.Notes.AsNoTracking().AsQueryable();

            if (jobId.HasValue)
            {
                query = query.Where(n => n.JobId == jobId.Value);
            }

            if (tradePackageId.HasValue)
            {
                query = query.Where(n => n.TradePackageId == tradePackageId.Value);
            }

            query = query.Where(n =>
                n.Visibility == "public"
                || (n.Visibility == "private" && n.CreatedByUserId == normalizedRequester)
                || (n.Visibility == "team-only" && visibilitySet.canViewTeamOnly)
            );

            var notes = await (
                from note in query
                join user in _context.Users on note.CreatedByUserId equals user.Id into userJoin
                from user in userJoin.DefaultIfEmpty()
                orderby note.CreatedAt descending
                select new
                {
                    note.Id,
                    note.JobId,
                    note.TradePackageId,
                    note.NoteText,
                    note.Visibility,
                    note.CreatedByUserId,
                    CreatedByDisplayName = user != null
                        ? string.Concat(
                                (user.FirstName ?? "").Trim(),
                                " ",
                                (user.LastName ?? "").Trim()
                            )
                            .Trim()
                        : note.CreatedByUserId,
                    note.CreatedAt,
                    note.UpdatedAt,
                }
            ).ToListAsync();

            return Ok(notes);
        }

        [HttpPost("marketplace")]
        public async Task<IActionResult> CreateMarketplaceNote(
            [FromBody] CreateMarketplaceNoteDto dto
        )
        {
            if (dto == null)
            {
                return BadRequest(new { error = "Payload is required." });
            }

            if (string.IsNullOrWhiteSpace(dto.CreatedByUserId))
            {
                return BadRequest(new { error = "createdByUserId is required." });
            }

            if (!dto.JobId.HasValue && !dto.TradePackageId.HasValue)
            {
                return BadRequest(new { error = "Either jobId or tradePackageId is required." });
            }

            var normalizedText = (dto.NoteText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return BadRequest(new { error = "noteText is required." });
            }

            if (normalizedText.Length > 2000)
            {
                return BadRequest(new { error = "noteText cannot exceed 2000 characters." });
            }

            var normalizedVisibility = NormalizeVisibility(dto.Visibility);
            var normalizedUser = dto.CreatedByUserId.Trim();

            if (dto.JobId.HasValue)
            {
                var jobExists = await _context.Jobs.AnyAsync(j => j.Id == dto.JobId.Value);
                if (!jobExists)
                {
                    return NotFound(new { error = "Job not found." });
                }
            }

            if (dto.TradePackageId.HasValue)
            {
                var tradePackage = await _context
                    .TradePackages.AsNoTracking()
                    .FirstOrDefaultAsync(tp => tp.Id == dto.TradePackageId.Value);
                if (tradePackage == null)
                {
                    return NotFound(new { error = "Trade package not found." });
                }

                if (!dto.JobId.HasValue)
                {
                    dto.JobId = tradePackage.JobId;
                }
            }

            var note = new NoteModel
            {
                JobId = dto.JobId,
                TradePackageId = dto.TradePackageId,
                CreatedByUserId = normalizedUser,
                NoteText = normalizedText,
                Visibility = normalizedVisibility,
                CreatedAt = DateTime.UtcNow,
            };

            _context.Notes.Add(note);
            await _context.SaveChangesAsync();

            var user = await _context
                .Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == normalizedUser);
            var displayName =
                user != null
                    ? string.Concat(
                            (user.FirstName ?? "").Trim(),
                            " ",
                            (user.LastName ?? "").Trim()
                        )
                        .Trim()
                    : normalizedUser;

            return Ok(
                new
                {
                    note.Id,
                    note.JobId,
                    note.TradePackageId,
                    note.NoteText,
                    note.Visibility,
                    note.CreatedByUserId,
                    CreatedByDisplayName = string.IsNullOrWhiteSpace(displayName)
                        ? note.CreatedByUserId
                        : displayName,
                    note.CreatedAt,
                    note.UpdatedAt,
                }
            );
        }

        [HttpPut("marketplace/{id:int}")]
        public async Task<IActionResult> UpdateMarketplaceNote(
            int id,
            [FromBody] UpdateMarketplaceNoteDto dto
        )
        {
            if (dto == null)
            {
                return BadRequest(new { error = "Payload is required." });
            }

            if (string.IsNullOrWhiteSpace(dto.RequesterUserId))
            {
                return BadRequest(new { error = "requesterUserId is required." });
            }

            var normalizedText = (dto.NoteText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return BadRequest(new { error = "noteText is required." });
            }

            if (normalizedText.Length > 2000)
            {
                return BadRequest(new { error = "noteText cannot exceed 2000 characters." });
            }

            var normalizedRequester = dto.RequesterUserId.Trim();
            var note = await _context.Notes.FirstOrDefaultAsync(n => n.Id == id);
            if (note == null)
            {
                return NotFound(new { error = "Note not found." });
            }

            if (!string.Equals(note.CreatedByUserId, normalizedRequester, StringComparison.Ordinal))
            {
                return Forbid();
            }

            note.NoteText = normalizedText;
            note.Visibility = NormalizeVisibility(dto.Visibility);
            note.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var user = await _context
                .Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == normalizedRequester);
            var displayName =
                user != null
                    ? string.Concat(
                            (user.FirstName ?? "").Trim(),
                            " ",
                            (user.LastName ?? "").Trim()
                        )
                        .Trim()
                    : normalizedRequester;

            return Ok(
                new
                {
                    note.Id,
                    note.JobId,
                    note.TradePackageId,
                    note.NoteText,
                    note.Visibility,
                    note.CreatedByUserId,
                    CreatedByDisplayName = string.IsNullOrWhiteSpace(displayName)
                        ? note.CreatedByUserId
                        : displayName,
                    note.CreatedAt,
                    note.UpdatedAt,
                }
            );
        }

        [HttpDelete("marketplace/{id:int}")]
        public async Task<IActionResult> DeleteMarketplaceNote(
            int id,
            [FromQuery] string requesterUserId
        )
        {
            if (string.IsNullOrWhiteSpace(requesterUserId))
            {
                return BadRequest(new { error = "requesterUserId is required." });
            }

            var normalizedRequester = requesterUserId.Trim();
            var note = await _context.Notes.FirstOrDefaultAsync(n => n.Id == id);
            if (note == null)
            {
                return NotFound(new { error = "Note not found." });
            }

            if (!string.Equals(note.CreatedByUserId, normalizedRequester, StringComparison.Ordinal))
            {
                return Forbid();
            }

            _context.Notes.Remove(note);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private static string NormalizeVisibility(string? rawVisibility)
        {
            var value = (rawVisibility ?? "private").Trim().ToLowerInvariant();
            return value switch
            {
                "public" => "public",
                "team-only" => "team-only",
                _ => "private",
            };
        }

        private async Task<(bool canViewTeamOnly, string ownerId)> ResolveVisibleScopesAsync(
            string requesterUserId,
            int? jobId,
            int? tradePackageId
        )
        {
            string ownerId = string.Empty;

            if (tradePackageId.HasValue)
            {
                ownerId = await _context
                    .TradePackages.AsNoTracking()
                    .Where(tp => tp.Id == tradePackageId.Value)
                    .Select(tp => tp.Job.UserId ?? string.Empty)
                    .FirstOrDefaultAsync();
            }

            if (string.IsNullOrWhiteSpace(ownerId) && jobId.HasValue)
            {
                ownerId = await _context
                    .Jobs.AsNoTracking()
                    .Where(j => j.Id == jobId.Value)
                    .Select(j => j.UserId ?? string.Empty)
                    .FirstOrDefaultAsync();
            }

            ownerId = (ownerId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                return (false, ownerId);
            }

            if (string.Equals(ownerId, requesterUserId, StringComparison.Ordinal))
            {
                return (true, ownerId);
            }

            var isTeamMember = await _context
                .TeamMembers.AsNoTracking()
                .AnyAsync(tm =>
                    tm.Id == requesterUserId
                    && tm.InviterId == ownerId
                    && (tm.Status == "Registered" || tm.Status == "Invited")
                );

            return (isTeamMember, ownerId);
        }
    }
}

