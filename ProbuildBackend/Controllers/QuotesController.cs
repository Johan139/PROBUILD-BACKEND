using System.Security.Cryptography;
using System.Text;
using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Helpers;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;
using IEmailSender = ProbuildBackend.Interface.IEmailSender;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/quotes")]
    public class QuotesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        UserManager<UserModel> _userManager;
        private readonly SubscriptionService _subscriptionService;
        private readonly AzureBlobService _azureBlobService;
        private readonly ILogger<QuotesController> _logger;
        private readonly IEmailSender _emailSender;
        private readonly IEmailTemplateService _emailTemplate;

        public QuotesController(
            ApplicationDbContext context,
            SubscriptionService subscriptionService,
            AzureBlobService azureBlobService,
            ILogger<QuotesController> logger,
            IEmailSender emailSender,
            IEmailTemplateService emailTemplate,
            UserManager<UserModel> userManager
        )
        {
            _context = context;
            _subscriptionService = subscriptionService;
            _azureBlobService = azureBlobService;
            _logger = logger;
            _emailSender = emailSender;
            _emailTemplate = emailTemplate;
            _userManager = userManager;
        }

        [HttpPost("upload")]
        [RequestSizeLimit(200 * 1024 * 1024)]
        public async Task<IActionResult> Upload([FromForm] UploadQuoteDto uploadQuoteDto)
        {
            if (
                uploadQuoteDto == null
                || uploadQuoteDto.Quote == null
                || !uploadQuoteDto.Quote.Any()
            )
            {
                return BadRequest(new { error = "No quote file provided" });
            }

            var quoteFile = uploadQuoteDto.Quote.First();
            var allowedExtensions = new[] { ".pdf" };
            var extension = System.IO.Path.GetExtension(quoteFile.FileName).ToLowerInvariant();

            if (Array.IndexOf(allowedExtensions, extension) < 0)
            {
                return BadRequest(
                    new { error = "Invalid file type. Only PDF files are allowed for quotes." }
                );
            }

            var uploadedFileUrls = await _azureBlobService.UploadFiles(
                uploadQuoteDto.Quote,
                null,
                null
            );

            var fileUrl = uploadedFileUrls.FirstOrDefault();
            if (fileUrl != null)
            {
                var jobDocument = new JobDocumentModel
                {
                    JobId = null,
                    FileName = System.IO.Path.GetFileName(new Uri(fileUrl).LocalPath),
                    BlobUrl = fileUrl,
                    SessionId = uploadQuoteDto.sessionId,
                    Type = "Quote",
                    UploadedAt = DateTime.Now,
                };
                _context.JobDocuments.Add(jobDocument);
                await _context.SaveChangesAsync();
            }

            var response = new { FileUrl = fileUrl };

            return Ok(response);
        }

        // ======================================================
        // SAVE DRAFT (CREATE OR NEW VERSION)
        // ======================================================
        [HttpPost("draft")]
        public async Task<IActionResult> SaveDraft([FromBody] QuoteDto dto)
        {
            var result = await SaveQuoteInternal(dto);

            return Ok(new { QuoteId = result.quote.Id, Version = result.version.Version });
        }

        private async Task<(Quote quote, QuoteVersionModel version)> SaveQuoteInternal(QuoteDto dto)
        {
            Quote quote;

            if (dto.QuoteId == null)
            {
                var lastQuote = await _context
                    .Quotes.Where(q => q.DocumentType == dto.DocumentType)
                    .OrderByDescending(q => q.CreatedDate)
                    .FirstOrDefaultAsync();

                var prefix = dto.DocumentType == "QUOTE" ? "Q" : "INV";
                var next = lastQuote?.Number?.Split('-').LastOrDefault();
                var number = int.TryParse(next, out var n)
                    ? $"{prefix}-{(n + 1):D3}"
                    : $"{prefix}-001";

                quote = new Quote
                {
                    Id = Guid.NewGuid(),
                    JobID = dto.JobID,
                    Number = number,
                    DocumentType = dto.DocumentType,
                    Status = "Draft",
                    CreatedBy = dto.CreatedBy,
                    CreatedID = dto.CreatedID,
                    CurrentVersion = 1,
                    CreatedDate = DateTime.UtcNow,
                };

                _context.Quotes.Add(quote);
            }
            else
            {
                quote =
                    await _context.Quotes.FindAsync(dto.QuoteId.Value)
                    ?? throw new Exception("Quote not found");

                if (quote.Status != "Draft")
                    throw new Exception("Cannot edit submitted quote");
            }

            var version = new QuoteVersionModel
            {
                Id = Guid.NewGuid(),
                QuoteId = quote.Id,
                Version = quote.CurrentVersion,
                Header = dto.DocumentType,
                From = dto.From,
                To = dto.To,
                Date = dto.Date,
                DueDate = dto.DueDate,
                Notes = dto.Notes,
                Terms = dto.Terms,
                Total = dto.Total,
                ClientAddress = dto.ClientAddress,
                ClientPhone = dto.ClientPhone,
                ClientEmail = dto.ClientEmail,
                ProjectName = dto.ProjectName,
                ProjectAddress = dto.ProjectAddress,
                PaymentTerms = dto.PaymentTerms,
                LogoId = dto.LogoId,
                CreatedDate = DateTime.UtcNow,
            };

            _context.QuoteVersions.Add(version);

            foreach (var row in dto.Rows)
            {
                _context.QuoteRows.Add(
                    new QuoteRow
                    {
                        Id = Guid.NewGuid(),
                        QuoteVersionId = version.Id,
                        Description = row.Description,
                        Quantity = row.Quantity,
                        Unit = row.Unit,
                        UnitPrice = row.UnitPrice,
                        Total = row.Total,
                    }
                );
            }

            foreach (var cost in dto.ExtraCosts)
            {
                _context.QuoteExtraCosts.Add(
                    new QuoteExtraCost
                    {
                        Id = Guid.NewGuid(),
                        QuoteVersionId = version.Id,
                        Type = cost.Type,
                        Value = cost.Value,
                        Title = cost.Title,
                    }
                );
            }

            quote.CurrentVersion++;

            await _context.SaveChangesAsync();

            return (quote, version);
        }

        // ======================================================
        // SUBMIT QUOTE (LOCK)
        // ======================================================
        [HttpPost("{quoteId}/submit")]
        public async Task<IActionResult> SubmitQuote(Guid quoteId)
        {
            var quote = await _context.Quotes.FindAsync(quoteId);
            if (quote == null)
                return NotFound();

            if (quote.Status != "Draft")
                return BadRequest("Quote already submitted.");

            var canSubmit = await _subscriptionService.CanSubmitQuote(quote.CreatedID);
            if (!canSubmit)
                return BadRequest("Quote submission limit reached.");

            quote.Status = "Submitted";
            string label = quote.DocumentType?.ToLower() == "invoice" ? "Invoice" : "Quote";

            string SubmitMessage = $"New {label} submitted: {quote.Number}";
            var quoteNotification = new NotificationModel
            {
                Message = $"{SubmitMessage}",
                JobId = quote.JobID,
                SenderId = quote.CreatedID,
                Recipients = new List<string> { quote.SentTo },
                Type = "Quote",
                QuoteId = quote.Id,
            };
            _context.Notifications.Add(quoteNotification);
            await _subscriptionService.IncrementQuoteCount(quote.CreatedID);
            await _context.SaveChangesAsync();

            return Ok();
        }

        // ======================================================
        // GET LATEST VERSION (EDITOR / PDF / VIEW)
        // ======================================================
        [HttpGet("{quoteId}")]
        public async Task<IActionResult> GetQuote(Guid quoteId)
        {
            try
            {
                var quote = await _context.Quotes.FindAsync(quoteId);
                if (quote == null)
                    return NotFound();

                var versionNumber = quote.CurrentVersion - 1;

                var version = await _context.QuoteVersions.FirstOrDefaultAsync(v =>
                    v.QuoteId == quoteId && v.Version == versionNumber
                );

                if (version == null)
                    return NotFound();

                var rows = await _context
                    .QuoteRows.Where(r => r.QuoteVersionId == version.Id)
                    .ToListAsync();

                var extras = await _context
                    .QuoteExtraCosts.Where(e => e.QuoteVersionId == version.Id)
                    .ToListAsync();

                // ✅ Load logo if exists (using Guid)
                LogosModel? logo = null;
                if (version.LogoId.HasValue)
                {
                    logo = await _context.Logos.FindAsync(version.LogoId.Value);
                }

                return Ok(
                    new QuoteViewDto
                    {
                        QuoteId = quote.Id,
                        Number = quote.Number,
                        Status = quote.Status,
                        DocumentType = quote.DocumentType,
                        CurrentVersion = version.Version,
                        CreatedID = quote.CreatedID,
                        SentTo = quote.SentTo,
                        Version = new QuoteVersionDto
                        {
                            Version = version.Version,
                            Header = version.Header,
                            From = version.From,
                            To = version.To,
                            Date = version.Date,
                            DueDate = version.DueDate,
                            Notes = version.Notes,
                            PaymentTerms = version.PaymentTerms,
                            Terms = version.Terms,
                            Total = version.Total,
                            ClientAddress = version.ClientAddress,
                            ClientEmail = version.ClientEmail,
                            ClientPhone = version.ClientPhone,
                            ProjectAddress = version.ProjectAddress,
                            ProjectName = version.ProjectName,
                            LogoId = version.LogoId, // ✅ Include logo ID (Guid)
                        },

                        LogoUrl = logo?.Url, // ✅ Use Url property from LogosModel

                        Rows = rows.Select(r => new QuoteRowDto
                            {
                                Description = r.Description,
                                Quantity = r.Quantity,
                                Unit = r.Unit,
                                UnitPrice = r.UnitPrice,
                                Total = r.Total,
                            })
                            .ToList(),

                        ExtraCosts = extras
                            .Select(e => new QuoteExtraCostDto
                            {
                                Type = e.Type,
                                Value = e.Value,
                                Title = e.Title,
                            })
                            .ToList(),
                    }
                );
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        // ======================================================
        // LIST USER QUOTES (LATEST ONLY)
        // ======================================================
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserQuotes(string userId)
        {
            var quotes = await _context
                .Quotes.Where(q =>
                    (q.CreatedID == userId || q.SentTo == userId) && q.ArchivedAt == null
                )
                .OrderByDescending(q => q.CreatedDate)
                .Select(q => new
                {
                    id = q.Id,
                    number = q.Number,
                    status = q.Status,
                    createdDate = q.CreatedDate,
                    createdBy = q.CreatedID,
                    sentTo = q.SentTo,
                    documentType = q.DocumentType,
                    direction = q.CreatedID == userId ? "Outbound" : "Inbound",

                    total = _context
                        .QuoteVersions.Where(v =>
                            v.QuoteId == q.Id && v.Version == q.CurrentVersion - 1
                        )
                        .Select(v => v.Total)
                        .FirstOrDefault(),

                    jobName = _context
                        .Jobs.Where(j => j.Id == q.JobID)
                        .Select(j => j.ProjectName)
                        .FirstOrDefault(),
                })
                .ToListAsync();

            return Ok(quotes);
        }

        // ======================================================
        // UPLOAD LOGO
        // ======================================================
        [HttpPost("logo/{userId}")]
        public async Task<IActionResult> UploadLogo(IFormFile file, string userId)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            if (!file.ContentType.StartsWith("image/"))
                return BadRequest("File must be an image");

            try
            {
                // ✅ Get the actual user ID (NOT email) from claims
                var userIdClaim =
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value
                    ?? User.FindFirst("id")?.Value;

                if (string.IsNullOrEmpty(userIdClaim))
                {
                    _logger.LogWarning("Unable to determine user ID from claims");
                    return Unauthorized("Unable to determine user identity");
                }

                // ✅ Extract just the GUID portion if it's a full URI

                if (userIdClaim.Contains("/"))
                {
                    userId = userIdClaim.Split('/').Last();
                }

                _logger.LogInformation("Uploading logo for user: {UserId}", userId);

                var blobUrl = await _azureBlobService.UploadImageAsync(
                    file,
                    $"logos/{userId}" // Now using clean user ID, not email
                );

                _logger.LogInformation("Logo uploaded successfully to: {BlobUrl}", blobUrl);

                var logo = new LogosModel
                {
                    Id = Guid.NewGuid(),
                    Url = blobUrl,
                    FileName = file.FileName,
                    UploadedBy = userId,
                    UploadedAt = DateTime.UtcNow,
                    Type = "quote-logo",
                };

                _context.Logos.Add(logo);
                await _context.SaveChangesAsync();

                return Ok(new { id = logo.Id, url = logo.Url });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading logo");
                return StatusCode(500, $"Error uploading logo: {ex.Message}");
            }
        }

        // ======================================================
        // GET LOGO BY ID
        // ======================================================
        [HttpGet("logo/{logoId}")]
        public async Task<IActionResult> GetLogo(Guid logoId) // ✅ Changed to Guid
        {
            var logo = await _context.Logos.FindAsync(logoId);
            if (logo == null)
                return NotFound();

            return Ok(
                new
                {
                    id = logo.Id,
                    url = logo.Url,
                    fileName = logo.FileName,
                }
            );
        }

        [HttpGet("logo/file/{logoId}")]
        public async Task<IActionResult> GetLogoFile(Guid logoId)
        {
            var logo = await _context.Logos.FindAsync(logoId);
            if (logo == null)
                return NotFound();

            var (stream, contentType, fileName) = await _azureBlobService.GetBlobContentAsync(
                logo.Url
            );

            return File(stream, contentType);
        }

        // ======================================================
        // GET USER'S DEFAULT LOGO
        // ======================================================
        [HttpGet("logo/user/{userId}")]
        public async Task<IActionResult> GetUserLogo(string userId)
        {
            var logo = await _context
                .Logos.Where(l => l.UploadedBy == userId && l.Type == "quote-logo")
                .OrderByDescending(l => l.UploadedAt)
                .FirstOrDefaultAsync();

            if (logo == null)
                return NotFound("No logo found for user");

            return Ok(
                new
                {
                    id = logo.Id,
                    url = logo.Url,
                    fileName = logo.FileName,
                }
            );
        }

        // ======================================================
        // SAVE + SEND TO CLIENT (SUBMIT + EMAIL)
        // ======================================================
        [HttpPost("save-and-send")]
        public async Task<IActionResult> SaveAndSend([FromBody] SaveAndSendQuoteDto payload)
        {
            try
            {
                if (payload?.Quote == null || payload.Send == null)
                    return BadRequest("Invalid payload");

                var quoteDto = payload.Quote;
                var sendDto = payload.Send;

                Quote quote;
                QuoteVersionModel latestVersion;

                // ======================================================
                // 1. SAVE ONLY IF NOT ALREADY SAVED
                // ======================================================
                if (quoteDto.QuoteId == null)
                {
                    // Not saved yet → save once
                    var saveResult = await SaveQuoteInternal(quoteDto);
                    quote = saveResult.quote;
                    latestVersion = saveResult.version;
                }
                else
                {
                    // Already saved → DO NOT save again
                    quote = await _context.Quotes.FirstOrDefaultAsync(q =>
                        q.Id == quoteDto.QuoteId.Value
                    );

                    if (quote == null)
                        return NotFound("Quote not found");

                    latestVersion = await _context
                        .QuoteVersions.Where(v => v.QuoteId == quote.Id)
                        .OrderByDescending(v => v.Version)
                        .FirstOrDefaultAsync();

                    if (latestVersion == null)
                        return BadRequest("Quote has no version");
                }

                // ======================================================
                // 2. ENSURE CLIENT USER EXISTS
                // ======================================================
                var clientEmail = !string.IsNullOrWhiteSpace(sendDto.ClientEmail)
                    ? sendDto.ClientEmail
                    : latestVersion.ClientEmail;

                if (string.IsNullOrWhiteSpace(clientEmail))
                    return BadRequest("Client email is required");

                var user = await _userManager.FindByEmailAsync(clientEmail);

                if (user == null)
                {
                    var normalizedEmail = clientEmail.ToUpperInvariant();

                    var placeholderUser = new UserModel
                    {
                        Id = Guid.NewGuid().ToString(),
                        UserName = clientEmail,
                        Email = clientEmail,
                        NormalizedUserName = normalizedEmail, // optional: handled automatically but safe
                        NormalizedEmail = normalizedEmail, // optional: handled automatically but safe
                        FirstName = "",
                        LastName = "",
                        PhoneNumber = "",
                        CompanyName = "",
                        CompanyRegNo = "",
                        VatNo = "",
                        UserType = "",
                        ConstructionType = null,
                        NrEmployees = "",
                        YearsOfOperation = "",
                        CertificationStatus = "",
                        CertificationDocumentPath = "",
                        Availability = "",
                        Trade = "",
                        ProductsOffered = "",
                        SupplierType = "",
                        JobPreferences = null,
                        DeliveryArea = null,
                        DeliveryTime = "",
                        //We need to move away from the below. It will cause confusion between the new address model and old.
                        //Country = countryId.Id.ToString();
                        //State = stateId.Id.ToString();
                        //City = model.City;
                        SubscriptionPackage = "",
                        DateCreated = DateTime.UtcNow,
                        CountryNumberCode = "",
                        isPlaceholder = true,
                    };

                    var result = await _userManager.CreateAsync(
                        placeholderUser,
                        PasswordGenerator.GenerateRandomPassword(12)
                    );

                    if (!result.Succeeded)
                        return StatusCode(500, "Failed to create placeholder user");

                    quote.SentTo = placeholderUser.Id;
                }
                else
                {
                    quote.SentTo = user.Id;
                }

                // ======================================================
                // 3. SUBSCRIPTION CHECK (ONLY IF DRAFT)
                // ======================================================
                if (quote.Status == "Draft")
                {
                    var canSubmit = await _subscriptionService.CanSubmitQuote(quote.CreatedID);
                    if (!canSubmit)
                        return BadRequest("Quote submission limit reached");

                    quote.Status = "Submitted";
                    await _subscriptionService.IncrementQuoteCount(quote.CreatedID);
                }
                string label = quote.DocumentType?.ToLower() == "invoice" ? "Invoice" : "Quote";

                string SubmitMessage = $"New {label} submitted: {quote.Number}";
                var quoteNotification = new NotificationModel
                {
                    Message = $"{SubmitMessage}",
                    JobId = quote.JobID,
                    SenderId = quote.CreatedID,
                    Recipients = new List<string> { quote.SentTo },
                    Type = "Quote",
                    QuoteId = quote.Id,
                };
                _context.Notifications.Add(quoteNotification);

                await _context.SaveChangesAsync();

                // ======================================================
                // 4. EMAIL + PDF
                // ======================================================
                var frontendUrl =
                    Environment.GetEnvironmentVariable("FRONTEND_URL")
                    ?? "https://app.probuildai.com";

                var quoteLink = $"{frontendUrl}/quote?quoteId={quote.Id}";

                var projectName = latestVersion.ProjectName ?? "Your Project";
                var clientName = !string.IsNullOrWhiteSpace(sendDto.ClientName)
                    ? sendDto.ClientName
                    : latestVersion.To ?? "Valued Customer";

                var emailTemplate = await _emailTemplate.GetTemplateAsync("NewQuoteSubmittedEmail");

                emailTemplate.Subject = emailTemplate.Subject.Replace(
                    "{{job.ProjectName}}",
                    projectName
                );

                emailTemplate.Body = emailTemplate
                    .Body.Replace("{{Header}}", emailTemplate.HeaderHtml)
                    .Replace("{{Footer}}", emailTemplate.FooterHtml)
                    .Replace("{{UserName}}", clientName)
                    .Replace("{{quote.Number}}", quote.Number)
                    .Replace("{{job.ProjectName}}", projectName)
                    .Replace("{{QuoteLink}}", quoteLink);

                var pdfBytes = await GenerateQuotePdf(quote, latestVersion, quote.Id);

                var docType = quote.DocumentType == "INVOICE" ? "Invoice" : "Quote";
                var pdfFileName = $"{docType}_{quote.Number}.pdf";

                await _emailSender.SendEmailWithAttachmentAsync(
                    emailTemplate,
                    clientEmail,
                    pdfBytes,
                    pdfFileName,
                    "application/pdf"
                );

                return Ok(
                    new
                    {
                        Success = true,
                        QuoteId = quote.Id,
                        Status = quote.Status,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving and sending quote");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{quoteId}/resend")]
        public async Task<IActionResult> ResendQuote(
            Guid quoteId,
            [FromBody] SendQuoteToClientDto dto
        )
        {
            try
            {
                var quote = await _context.Quotes.FindAsync(quoteId);
                if (quote == null)
                    return NotFound("Quote not found");

                var latestVersion = await _context
                    .QuoteVersions.Where(v => v.QuoteId == quoteId)
                    .OrderByDescending(v => v.Version)
                    .FirstOrDefaultAsync();

                if (latestVersion == null)
                    return BadRequest("Quote has no version");

                // Resolve email
                var clientEmail = !string.IsNullOrWhiteSpace(dto.ClientEmail)
                    ? dto.ClientEmail
                    : latestVersion.ClientEmail;

                if (string.IsNullOrWhiteSpace(clientEmail))
                    return BadRequest("Client email is required");

                // Link user if needed (NO creation on resend)
                var user = await _userManager.FindByEmailAsync(clientEmail);
                if (user != null)
                    quote.SentTo = user.Id;

                await _context.SaveChangesAsync();

                // Build email
                var frontendUrl =
                    Environment.GetEnvironmentVariable("FRONTEND_URL")
                    ?? "https://app.probuildai.com";

                var quoteLink = $"{frontendUrl}/quote?quoteId={quote.Id}";

                var projectName = latestVersion.ProjectName ?? "Your Project";
                var clientName = dto.ClientName ?? latestVersion.To ?? "Valued Customer";

                var emailTemplate = await _emailTemplate.GetTemplateAsync("NewQuoteSubmittedEmail");

                emailTemplate.Subject = emailTemplate.Subject.Replace(
                    "{{job.ProjectName}}",
                    projectName
                );

                emailTemplate.Body = emailTemplate
                    .Body.Replace("{{Header}}", emailTemplate.HeaderHtml)
                    .Replace("{{Footer}}", emailTemplate.FooterHtml)
                    .Replace("{{UserName}}", clientName)
                    .Replace("{{quote.Number}}", quote.Number)
                    .Replace("{{job.ProjectName}}", projectName)
                    .Replace("{{QuoteLink}}", quoteLink);

                var pdfBytes = await GenerateQuotePdf(quote, latestVersion, quote.Id);

                var docType = quote.DocumentType == "INVOICE" ? "Invoice" : "Quote";
                var pdfFileName = $"{docType}_{quote.Number}.pdf";

                await _emailSender.SendEmailWithAttachmentAsync(
                    emailTemplate,
                    clientEmail,
                    pdfBytes,
                    pdfFileName,
                    "application/pdf"
                );

                return Ok(
                    new
                    {
                        Success = true,
                        QuoteId = quote.Id,
                        Status = quote.Status,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resending quote {QuoteId}", quoteId);
                return StatusCode(500, ex.Message);
            }
        }

        public static string GenerateRandomPassword(int length = 12)
        {
            const string chars =
                "ABCDEFGHJKLMNPQRSTUVWXYZ"
                + // no confusing chars
                "abcdefghijkmnopqrstuvwxyz"
                + "23456789"
                + "!@#$%^&*_-+=";

            var result = new StringBuilder(length);
            var bytes = new byte[length];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            for (int i = 0; i < length; i++)
            {
                result.Append(chars[bytes[i] % chars.Length]);
            }

            return result.ToString();
        }

        private async Task<byte[]> GenerateQuotePdf(
            Quote quote,
            QuoteVersionModel version,
            Guid quoteId
        )
        {
            // Get quote rows
            var rows = await _context
                .QuoteRows.Where(r => r.QuoteVersionId == version.Id)
                .ToListAsync();

            // Get extra costs
            var extraCosts = await _context
                .QuoteExtraCosts.Where(e => e.QuoteVersionId == version.Id)
                .ToListAsync();

            // Get logo if exists
            string? logoUrl = null;
            if (version.LogoId.HasValue)
            {
                var logo = await _context.Logos.FindAsync(version.LogoId.Value);
                logoUrl = logo?.Url;
            }

            var docType = quote.DocumentType == "INVOICE" ? "INVOICE" : "QUOTE";
            var docTypeLabel = quote.DocumentType == "INVOICE" ? "Invoice" : "Quote";

            // Create the PDF in memory
            var ms = new MemoryStream();
            var writer = new PdfWriter(ms);
            var pdf = new PdfDocument(writer);
            var document = new Document(pdf, PageSize.A4);

            document.SetMargins(40, 40, 40, 40);

            // Define fonts
            var boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var regularFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            // Define colors (using your brand colors)
            var primaryYellow = new DeviceRgb(251, 208, 8); // #fbd008
            var darkerYellow = new DeviceRgb(230, 191, 0); // #e6bf00
            var darkGray = new DeviceRgb(51, 51, 51); // #333333
            var mediumGray = new DeviceRgb(102, 102, 102); // #666666
            var lightGray = new DeviceRgb(248, 249, 250); // #f8f9fa
            var borderGray = new DeviceRgb(224, 224, 224); // #e0e0e0
            var black = new DeviceRgb(0, 0, 0);
            var white = new DeviceRgb(255, 255, 255);
            var dangerRed = new DeviceRgb(220, 53, 69); // #dc3545

            // === HEADER SECTION ===
            var headerTable = new Table(
                UnitValue.CreatePercentArray(new float[] { 55, 45 })
            ).UseAllAvailableWidth();

            // Left: Company Logo + Info
            var leftCell = new Cell()
                .SetBorder(Border.NO_BORDER)
                .SetVerticalAlignment(VerticalAlignment.TOP);

            // Add logo if exists
            if (!string.IsNullOrEmpty(logoUrl))
            {
                try
                {
                    var (logoStream, _, _) = await _azureBlobService.GetBlobContentAsync(logoUrl);
                    var logoMs = new MemoryStream();
                    await logoStream.CopyToAsync(logoMs);
                    var logoImage = new Image(ImageDataFactory.Create(logoMs.ToArray()))
                        .SetMaxWidth(120)
                        .SetMaxHeight(60)
                        .SetMarginBottom(12);
                    leftCell.Add(logoImage);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to load logo for PDF: {Error}", ex.Message);
                }
            }

            // Company name
            leftCell.Add(
                new Paragraph(version.From ?? "")
                    .SetFont(boldFont)
                    .SetFontSize(11)
                    .SetFontColor(darkGray)
                    .SetMarginBottom(4)
            );

            // Company address (from ClientAddress if available, otherwise use From field context)
            // Note: You might want to store company address separately

            headerTable.AddCell(leftCell);

            // Right: Document Title + Meta
            var rightCell = new Cell()
                .SetBorder(Border.NO_BORDER)
                .SetTextAlignment(TextAlignment.RIGHT)
                .SetVerticalAlignment(VerticalAlignment.TOP);

            // Document title with brand color
            rightCell.Add(
                new Paragraph(docType)
                    .SetFont(boldFont)
                    .SetFontSize(32)
                    .SetFontColor(primaryYellow)
                    .SetCharacterSpacing(2)
                    .SetMarginBottom(16)
            );

            // Meta info table for alignment
            var metaTable = new Table(
                UnitValue.CreatePercentArray(new float[] { 45, 55 })
            ).SetWidth(UnitValue.CreatePercentValue(100));

            // Quote/Invoice #
            metaTable.AddCell(
                new Cell()
                    .Add(
                        new Paragraph($"{docTypeLabel} #:")
                            .SetFont(regularFont)
                            .SetFontSize(9)
                            .SetFontColor(mediumGray)
                    )
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.RIGHT)
                    .SetPaddingBottom(4)
            );
            metaTable.AddCell(
                new Cell()
                    .Add(
                        new Paragraph(quote.Number ?? "DRAFT")
                            .SetFont(boldFont)
                            .SetFontSize(9)
                            .SetFontColor(darkGray)
                    )
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.LEFT)
                    .SetPaddingLeft(12)
                    .SetPaddingBottom(4)
            );

            // Date
            metaTable.AddCell(
                new Cell()
                    .Add(
                        new Paragraph("Date:")
                            .SetFont(regularFont)
                            .SetFontSize(9)
                            .SetFontColor(mediumGray)
                    )
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.RIGHT)
                    .SetPaddingBottom(4)
            );
            metaTable.AddCell(
                new Cell()
                    .Add(
                        new Paragraph(version.Date?.ToString("MMM dd, yyyy") ?? "N/A")
                            .SetFont(boldFont)
                            .SetFontSize(9)
                            .SetFontColor(darkGray)
                    )
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.LEFT)
                    .SetPaddingLeft(12)
                    .SetPaddingBottom(4)
            );

            // Valid Until / Due Date
            var dueDateLabel = quote.DocumentType == "INVOICE" ? "Due Date:" : "Valid Until:";
            metaTable.AddCell(
                new Cell()
                    .Add(
                        new Paragraph(dueDateLabel)
                            .SetFont(regularFont)
                            .SetFontSize(9)
                            .SetFontColor(mediumGray)
                    )
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.RIGHT)
                    .SetPaddingBottom(4)
            );
            metaTable.AddCell(
                new Cell()
                    .Add(
                        new Paragraph(version.DueDate?.ToString("MMM dd, yyyy") ?? "N/A")
                            .SetFont(boldFont)
                            .SetFontSize(9)
                            .SetFontColor(darkGray)
                    )
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.LEFT)
                    .SetPaddingLeft(12)
                    .SetPaddingBottom(4)
            );

            if (quote.DocumentType == "INVOICE" && !string.IsNullOrEmpty(version.PaymentTerms))
            {
                metaTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph("Payment Terms:")
                                .SetFont(regularFont)
                                .SetFontSize(9)
                                .SetFontColor(mediumGray)
                        )
                        .SetBorder(Border.NO_BORDER)
                        .SetTextAlignment(TextAlignment.RIGHT)
                        .SetPaddingBottom(4)
                );
                metaTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph(version.PaymentTerms)
                                .SetFont(boldFont)
                                .SetFontSize(9)
                                .SetFontColor(darkGray)
                        )
                        .SetBorder(Border.NO_BORDER)
                        .SetTextAlignment(TextAlignment.LEFT)
                        .SetPaddingLeft(12)
                        .SetPaddingBottom(4)
                );
            }

            rightCell.Add(metaTable);
            headerTable.AddCell(rightCell);
            document.Add(headerTable);

            // === DIVIDER ===
            document.Add(
                new Paragraph("")
                    .SetMarginTop(16)
                    .SetMarginBottom(16)
                    .SetBorderBottom(new SolidBorder(borderGray, 1))
            );

            // === BILL TO / PROJECT SECTION ===
            var detailsTable = new Table(UnitValue.CreatePercentArray(new float[] { 50, 50 }))
                .UseAllAvailableWidth()
                .SetMarginBottom(16);

            // Bill To
            var billToCell = new Cell().SetBorder(Border.NO_BORDER).SetPaddingRight(20);

            billToCell.Add(
                new Paragraph("BILL TO")
                    .SetFont(boldFont)
                    .SetFontSize(8)
                    .SetFontColor(mediumGray)
                    .SetCharacterSpacing(0.5f)
                    .SetMarginBottom(8)
            );

            billToCell.Add(
                new Paragraph(version.To ?? "Client Name")
                    .SetFont(boldFont)
                    .SetFontSize(10)
                    .SetFontColor(darkGray)
                    .SetMarginBottom(4)
            );

            if (!string.IsNullOrEmpty(version.ClientAddress))
                billToCell.Add(
                    new Paragraph(version.ClientAddress)
                        .SetFont(regularFont)
                        .SetFontSize(9)
                        .SetFontColor(mediumGray)
                        .SetMarginBottom(2)
                );

            if (!string.IsNullOrEmpty(version.ClientPhone))
                billToCell.Add(
                    new Paragraph(version.ClientPhone)
                        .SetFont(regularFont)
                        .SetFontSize(9)
                        .SetFontColor(mediumGray)
                        .SetMarginBottom(2)
                );

            if (!string.IsNullOrEmpty(version.ClientEmail))
                billToCell.Add(
                    new Paragraph(version.ClientEmail)
                        .SetFont(regularFont)
                        .SetFontSize(9)
                        .SetFontColor(mediumGray)
                );

            detailsTable.AddCell(billToCell);

            // Project
            var projectCell = new Cell().SetBorder(Border.NO_BORDER).SetPaddingLeft(20);

            if (
                !string.IsNullOrEmpty(version.ProjectName)
                || !string.IsNullOrEmpty(version.ProjectAddress)
            )
            {
                projectCell.Add(
                    new Paragraph("PROJECT")
                        .SetFont(boldFont)
                        .SetFontSize(8)
                        .SetFontColor(mediumGray)
                        .SetCharacterSpacing(0.5f)
                        .SetMarginBottom(8)
                );

                if (!string.IsNullOrEmpty(version.ProjectName))
                    projectCell.Add(
                        new Paragraph(version.ProjectName)
                            .SetFont(boldFont)
                            .SetFontSize(10)
                            .SetFontColor(darkGray)
                            .SetMarginBottom(4)
                    );

                if (!string.IsNullOrEmpty(version.ProjectAddress))
                    projectCell.Add(
                        new Paragraph(version.ProjectAddress)
                            .SetFont(regularFont)
                            .SetFontSize(9)
                            .SetFontColor(mediumGray)
                    );
            }

            detailsTable.AddCell(projectCell);
            document.Add(detailsTable);

            // === DIVIDER ===
            document.Add(
                new Paragraph("")
                    .SetMarginTop(8)
                    .SetMarginBottom(16)
                    .SetBorderBottom(new SolidBorder(borderGray, 1))
            );

            // === LINE ITEMS TABLE ===
            var itemsTable = new Table(
                UnitValue.CreatePercentArray(new float[] { 40, 10, 10, 20, 20 })
            )
                .UseAllAvailableWidth()
                .SetMarginTop(8);

            // Header row
            var headers = new[] { "Description", "Qty", "Unit", "Unit Price", "Amount" };
            var headerAlignments = new[]
            {
                TextAlignment.LEFT,
                TextAlignment.CENTER,
                TextAlignment.CENTER,
                TextAlignment.RIGHT,
                TextAlignment.RIGHT,
            };

            for (int i = 0; i < headers.Length; i++)
            {
                itemsTable.AddHeaderCell(
                    new Cell()
                        .Add(
                            new Paragraph(headers[i])
                                .SetFont(boldFont)
                                .SetFontSize(9)
                                .SetFontColor(darkGray)
                        )
                        .SetBackgroundColor(lightGray)
                        .SetBorder(Border.NO_BORDER)
                        .SetBorderBottom(new SolidBorder(borderGray, 2))
                        .SetPadding(12)
                        .SetTextAlignment(headerAlignments[i])
                );
            }

            // Data rows
            foreach (var row in rows)
            {
                // Description
                itemsTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph(row.Description ?? "")
                                .SetFont(regularFont)
                                .SetFontSize(9)
                                .SetFontColor(mediumGray)
                        )
                        .SetBorder(Border.NO_BORDER)
                        .SetBorderBottom(new SolidBorder(new DeviceRgb(238, 238, 238), 1))
                        .SetPadding(12)
                        .SetTextAlignment(TextAlignment.LEFT)
                );

                // Quantity
                itemsTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph(row.Quantity.ToString("N0"))
                                .SetFont(regularFont)
                                .SetFontSize(9)
                                .SetFontColor(mediumGray)
                        )
                        .SetBorder(Border.NO_BORDER)
                        .SetBorderBottom(new SolidBorder(new DeviceRgb(238, 238, 238), 1))
                        .SetPadding(12)
                        .SetTextAlignment(TextAlignment.CENTER)
                );

                // Unit
                itemsTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph(row.Unit ?? "")
                                .SetFont(regularFont)
                                .SetFontSize(9)
                                .SetFontColor(mediumGray)
                        )
                        .SetBorder(Border.NO_BORDER)
                        .SetBorderBottom(new SolidBorder(new DeviceRgb(238, 238, 238), 1))
                        .SetPadding(12)
                        .SetTextAlignment(TextAlignment.CENTER)
                );

                // Unit Price
                itemsTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph($"${row.UnitPrice:N2}")
                                .SetFont(regularFont)
                                .SetFontSize(9)
                                .SetFontColor(mediumGray)
                        )
                        .SetBorder(Border.NO_BORDER)
                        .SetBorderBottom(new SolidBorder(new DeviceRgb(238, 238, 238), 1))
                        .SetPadding(12)
                        .SetTextAlignment(TextAlignment.RIGHT)
                );

                // Amount
                itemsTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph($"${row.Total:N2}")
                                .SetFont(regularFont)
                                .SetFontSize(9)
                                .SetFontColor(mediumGray)
                        )
                        .SetBorder(Border.NO_BORDER)
                        .SetBorderBottom(new SolidBorder(new DeviceRgb(238, 238, 238), 1))
                        .SetPadding(12)
                        .SetTextAlignment(TextAlignment.RIGHT)
                );
            }

            // Empty state if no rows
            if (!rows.Any())
            {
                var italicFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);

                itemsTable.AddCell(
                    new Cell(1, 5)
                        .Add(
                            new Paragraph("No line items added")
                                .SetFont(italicFont)
                                .SetFontSize(9)
                                .SetFontColor(mediumGray)
                        )
                        .SetBorder(Border.NO_BORDER)
                        .SetPadding(20)
                        .SetTextAlignment(TextAlignment.CENTER)
                );
            }

            document.Add(itemsTable);

            // === TOTALS SECTION ===
            var totalsOuterTable = new Table(UnitValue.CreatePercentArray(new float[] { 60, 40 }))
                .UseAllAvailableWidth()
                .SetMarginTop(20);

            // Empty left cell
            totalsOuterTable.AddCell(new Cell().SetBorder(Border.NO_BORDER));

            // Right cell with totals
            var totalsCell = new Cell().SetBorder(Border.NO_BORDER);

            var totalsTable = new Table(
                UnitValue.CreatePercentArray(new float[] { 50, 50 })
            ).UseAllAvailableWidth();

            // Subtotal
            var subtotal = rows.Sum(r => r.Total);
            totalsTable.AddCell(
                new Cell()
                    .Add(
                        new Paragraph("Subtotal:")
                            .SetFont(regularFont)
                            .SetFontSize(9)
                            .SetFontColor(mediumGray)
                    )
                    .SetBorder(Border.NO_BORDER)
                    .SetPadding(8)
                    .SetTextAlignment(TextAlignment.LEFT)
            );
            totalsTable.AddCell(
                new Cell()
                    .Add(
                        new Paragraph($"${subtotal:N2}")
                            .SetFont(boldFont)
                            .SetFontSize(9)
                            .SetFontColor(darkGray)
                    )
                    .SetBorder(Border.NO_BORDER)
                    .SetPadding(8)
                    .SetTextAlignment(TextAlignment.RIGHT)
            );

            // Process extra costs (excluding AmountPaid - handled separately for invoices)
            decimal runningTotal = subtotal;

            foreach (
                var cost in extraCosts
                    .Where(c => c.Type != "AmountPaid")
                    .OrderBy(c =>
                        c.Type == "Extra" ? 0
                        : c.Type == "Discount" ? 1
                        : c.Type == "Tax" ? 2
                        : 3
                    )
            )
            {
                string label;
                string valueDisplay;
                bool isNegative = false;

                switch (cost.Type)
                {
                    case "Extra":
                        label = !string.IsNullOrEmpty(cost.Title)
                            ? $"{cost.Title}:"
                            : "Extra Cost:";
                        valueDisplay = $"${cost.Value:N2}";
                        runningTotal += cost.Value;
                        break;
                    case "Discount":
                        label = $"Discount ({cost.Value}%):";
                        var discountAmount = subtotal * (cost.Value / 100);
                        valueDisplay = $"-${discountAmount:N2}";
                        isNegative = true;
                        runningTotal -= discountAmount;
                        break;
                    case "Tax":
                        label = $"Tax ({cost.Value}%):";
                        var taxAmount = runningTotal * (cost.Value / 100);
                        valueDisplay = $"${taxAmount:N2}";
                        runningTotal += taxAmount;
                        break;
                    case "Flat":
                        label = "Flat Total:";
                        valueDisplay = $"${cost.Value:N2}";
                        runningTotal = cost.Value;
                        break;
                    default:
                        label = $"{cost.Title ?? cost.Type}:";
                        valueDisplay = $"${cost.Value:N2}";
                        break;
                }

                totalsTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph(label)
                                .SetFont(regularFont)
                                .SetFontSize(9)
                                .SetFontColor(mediumGray)
                        )
                        .SetBorder(Border.NO_BORDER)
                        .SetPadding(8)
                        .SetTextAlignment(TextAlignment.LEFT)
                );
                totalsTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph(valueDisplay)
                                .SetFont(boldFont)
                                .SetFontSize(9)
                                .SetFontColor(isNegative ? dangerRed : darkGray)
                        )
                        .SetBorder(Border.NO_BORDER)
                        .SetPadding(8)
                        .SetTextAlignment(TextAlignment.RIGHT)
                );
            }

            // Divider before total
            totalsTable.AddCell(
                new Cell(1, 2)
                    .SetBorder(Border.NO_BORDER)
                    .SetBorderBottom(new SolidBorder(borderGray, 1))
                    .SetPadding(4)
            );

            // Grand Total with brand color background
            totalsTable.AddCell(
                new Cell()
                    .Add(
                        new Paragraph("Total:")
                            .SetFont(boldFont)
                            .SetFontSize(12)
                            .SetFontColor(black)
                    )
                    .SetBackgroundColor(primaryYellow)
                    .SetBorder(Border.NO_BORDER)
                    .SetPadding(12)
                    .SetTextAlignment(TextAlignment.LEFT)
            );
            totalsTable.AddCell(
                new Cell()
                    .Add(
                        new Paragraph($"${version.Total:N2}")
                            .SetFont(boldFont)
                            .SetFontSize(12)
                            .SetFontColor(black)
                    )
                    .SetBackgroundColor(primaryYellow)
                    .SetBorder(Border.NO_BORDER)
                    .SetPadding(12)
                    .SetTextAlignment(TextAlignment.RIGHT)
            );

            // === INVOICE ONLY: Amount Paid & Balance Due ===
            var amountPaidCost = extraCosts.FirstOrDefault(c => c.Type == "AmountPaid");
            if (
                quote.DocumentType == "INVOICE"
                && amountPaidCost != null
                && amountPaidCost.Value > 0
            )
            {
                // Amount Paid row
                totalsTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph("Amount Paid:")
                                .SetFont(regularFont)
                                .SetFontSize(10)
                                .SetFontColor(mediumGray)
                        )
                        .SetBorder(Border.NO_BORDER)
                        .SetPadding(8)
                        .SetTextAlignment(TextAlignment.LEFT)
                );
                totalsTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph($"-${amountPaidCost.Value:N2}")
                                .SetFont(boldFont)
                                .SetFontSize(10)
                                .SetFontColor(new DeviceRgb(40, 167, 69))
                        ) // Green
                        .SetBorder(Border.NO_BORDER)
                        .SetPadding(8)
                        .SetTextAlignment(TextAlignment.RIGHT)
                );

                // Calculate balance due
                var balanceDue = version.Total - amountPaidCost.Value;

                // Balance Due row
                var balanceColor = balanceDue > 0 ? dangerRed : new DeviceRgb(40, 167, 69);
                var balanceLabel = balanceDue > 0 ? "Balance Due:" : "Paid in Full:";

                totalsTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph(balanceLabel)
                                .SetFont(boldFont)
                                .SetFontSize(12)
                                .SetFontColor(white)
                        )
                        .SetBackgroundColor(balanceColor)
                        .SetBorder(Border.NO_BORDER)
                        .SetPadding(12)
                        .SetTextAlignment(TextAlignment.LEFT)
                );
                totalsTable.AddCell(
                    new Cell()
                        .Add(
                            new Paragraph($"${Math.Abs(balanceDue):N2}")
                                .SetFont(boldFont)
                                .SetFontSize(12)
                                .SetFontColor(white)
                        )
                        .SetBackgroundColor(balanceColor)
                        .SetBorder(Border.NO_BORDER)
                        .SetPadding(12)
                        .SetTextAlignment(TextAlignment.RIGHT)
                );
            }

            // Add totals to document
            totalsCell.Add(totalsTable);
            totalsOuterTable.AddCell(totalsCell);
            document.Add(totalsOuterTable);
            // === NOTES & TERMS ===
            if (!string.IsNullOrEmpty(version.Notes) || !string.IsNullOrEmpty(version.Terms))
            {
                document.Add(
                    new Paragraph("")
                        .SetMarginTop(24)
                        .SetMarginBottom(16)
                        .SetBorderBottom(new SolidBorder(borderGray, 1))
                );
            }

            if (!string.IsNullOrEmpty(version.Notes))
            {
                document.Add(
                    new Paragraph("NOTES")
                        .SetFont(boldFont)
                        .SetFontSize(8)
                        .SetFontColor(mediumGray)
                        .SetCharacterSpacing(0.5f)
                        .SetMarginTop(16)
                        .SetMarginBottom(8)
                );
                document.Add(
                    new Paragraph(version.Notes)
                        .SetFont(regularFont)
                        .SetFontSize(9)
                        .SetFontColor(mediumGray)
                        .SetMultipliedLeading(1.6f)
                        .SetMarginBottom(16)
                );
            }

            if (!string.IsNullOrEmpty(version.Terms))
            {
                document.Add(
                    new Paragraph("TERMS & CONDITIONS")
                        .SetFont(boldFont)
                        .SetFontSize(8)
                        .SetFontColor(mediumGray)
                        .SetCharacterSpacing(0.5f)
                        .SetMarginTop(16)
                        .SetMarginBottom(8)
                );
                document.Add(
                    new Paragraph(version.Terms)
                        .SetFont(regularFont)
                        .SetFontSize(9)
                        .SetFontColor(mediumGray)
                        .SetMultipliedLeading(1.6f)
                );
            }

            // === FOOTER ===
            document.Add(
                new Paragraph("").SetMarginTop(32).SetBorderTop(new SolidBorder(borderGray, 1))
            );

            document.Add(
                new Paragraph("Generated by ProBuildAI")
                    .SetFont(regularFont)
                    .SetFontSize(8)
                    .SetFontColor(new DeviceRgb(153, 153, 153))
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetMarginTop(16)
            );

            // Close document to flush content to stream
            document.Close();

            // Return the bytes
            return ms.ToArray();
        }

        [HttpGet("{quoteId}/pdf")]
        public async Task<IActionResult> DownloadPdf(Guid quoteId)
        {
            var quote = await _context.Quotes.FindAsync(quoteId);
            if (quote == null)
                return NotFound();

            var version = await _context
                .QuoteVersions.Where(v => v.QuoteId == quoteId)
                .OrderByDescending(v => v.Version)
                .FirstOrDefaultAsync();

            if (version == null)
                return BadRequest("Quote has no version");

            var pdfBytes = await GenerateQuotePdf(quote, version, quoteId);

            var docType = quote.DocumentType == "INVOICE" ? "Invoice" : "Quote";
            var fileName = $"{docType}_{quote.Number}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }

        // ======================================================
        // DELETE QUOTE (DRAFT ONLY)
        // ======================================================
        [HttpDelete("{quoteId}")]
        public async Task<IActionResult> DeleteQuote(Guid quoteId)
        {
            try
            {
                var quote = await _context
                    .Quotes.Include(q => q.Versions)
                    .FirstOrDefaultAsync(q => q.Id == quoteId);

                if (quote == null)
                    return NotFound("Quote not found");

                // Safety: only allow deleting drafts
                if (quote.Status != "Draft")
                    return BadRequest("Only draft quotes can be deleted");

                _context.Quotes.Remove(quote);
                await _context.SaveChangesAsync();

                return Ok(new { Success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get quote {QuoteId}", quoteId);
                throw; // Or return appropriate error response
            }
        }

        // ======================================================
        // DUPLICATE QUOTE (LATEST VERSION ONLY)
        // ======================================================
        [HttpPost("{quoteId}/duplicate")]
        public async Task<IActionResult> DuplicateQuote(Guid quoteId)
        {
            var originalQuote = await _context.Quotes.FirstOrDefaultAsync(q => q.Id == quoteId);

            if (originalQuote == null)
                return NotFound("Quote not found");

            // Get latest version
            var latestVersionNumber = originalQuote.CurrentVersion - 1;

            var originalVersion = await _context.QuoteVersions.FirstOrDefaultAsync(v =>
                v.QuoteId == quoteId && v.Version == latestVersionNumber
            );

            if (originalVersion == null)
                return BadRequest("Quote has no version to duplicate");

            // Generate new quote number
            var lastQuote = await _context
                .Quotes.Where(q => q.DocumentType == originalQuote.DocumentType)
                .OrderByDescending(q => q.CreatedDate)
                .FirstOrDefaultAsync();

            string newNumber;
            if (lastQuote != null && lastQuote.Number?.Contains('-') == true)
            {
                var parts = lastQuote.Number.Split('-');
                var prefix = parts[0];
                var next = int.TryParse(parts[1], out var n) ? n + 1 : 1;
                newNumber = $"{prefix}-{next:D3}";
            }
            else
            {
                newNumber = originalQuote.DocumentType == "INVOICE" ? "INV-001" : "Q-001";
            }

            // Create new quote
            var newQuote = new Quote
            {
                Id = Guid.NewGuid(),
                JobID = originalQuote.JobID,
                DocumentType = originalQuote.DocumentType,
                Number = newNumber,
                Status = "Draft",
                CreatedBy = originalQuote.CreatedBy,
                CreatedID = originalQuote.CreatedID,
                CurrentVersion = 2,
                CreatedDate = DateTime.UtcNow,
            };

            _context.Quotes.Add(newQuote);

            // Create version 1
            var newVersion = new QuoteVersionModel
            {
                Id = Guid.NewGuid(),
                QuoteId = newQuote.Id,
                Version = 1,

                Header = originalVersion.Header,
                From = originalVersion.From,
                To = originalVersion.To,
                ClientAddress = originalVersion.ClientAddress,
                ClientPhone = originalVersion.ClientPhone,
                ClientEmail = originalVersion.ClientEmail,
                ProjectName = originalVersion.ProjectName,
                ProjectAddress = originalVersion.ProjectAddress,
                Date = DateTime.UtcNow,
                DueDate = originalVersion.DueDate,
                Notes = originalVersion.Notes,
                Terms = originalVersion.Terms,
                Total = originalVersion.Total,
                LogoId = originalVersion.LogoId,
                CreatedDate = DateTime.UtcNow,
            };

            _context.QuoteVersions.Add(newVersion);

            // Copy rows
            var rows = await _context
                .QuoteRows.Where(r => r.QuoteVersionId == originalVersion.Id)
                .ToListAsync();

            foreach (var row in rows)
            {
                _context.QuoteRows.Add(
                    new QuoteRow
                    {
                        Id = Guid.NewGuid(),
                        QuoteVersionId = newVersion.Id,
                        Description = row.Description,
                        Quantity = row.Quantity,
                        Unit = row.Unit,
                        UnitPrice = row.UnitPrice,
                        Total = row.Total,
                    }
                );
            }

            // Copy extra costs
            var extras = await _context
                .QuoteExtraCosts.Where(e => e.QuoteVersionId == originalVersion.Id)
                .ToListAsync();

            foreach (var extra in extras)
            {
                _context.QuoteExtraCosts.Add(
                    new QuoteExtraCost
                    {
                        Id = Guid.NewGuid(),
                        QuoteVersionId = newVersion.Id,
                        Type = extra.Type,
                        Title = extra.Title,
                        Value = extra.Value,
                    }
                );
            }

            await _context.SaveChangesAsync();

            return Ok(new { QuoteId = newQuote.Id, Number = newQuote.Number });
        }

        // ======================================================
        // CHANGE STATUS (APPROVE / REJECT)
        // ======================================================
        [HttpPost("{quoteId}/status")]
        public async Task<IActionResult> ChangeStatus(Guid quoteId, [FromBody] string status)
        {
            var quote = await _context.Quotes.FindAsync(quoteId);
            if (quote == null)
                return NotFound();
            var quoteNotification = new NotificationModel();
            string label = quote.DocumentType?.ToLower() == "invoice" ? "Invoice" : "Quote";

            string ApprovedMessage = $"{label} Approved: {quote.Number}";
            string RejectMessage = $"{label} Rejected: {quote.Number}";
            switch (status)
            {
                case "Approved":
                    quoteNotification = new NotificationModel
                    {
                        Message = $"{ApprovedMessage}",
                        JobId = quote.JobID,
                        SenderId = quote.SentTo,
                        Recipients = new List<string> { quote.CreatedID },
                        Type = "Quote",
                        QuoteId = quote.Id,
                    };

                    break;
                case "Rejected":
                    quoteNotification = new NotificationModel
                    {
                        Message = $"{RejectMessage}",
                        JobId = quote.JobID,
                        SenderId = quote.SentTo,
                        Recipients = new List<string> { quote.CreatedID },
                        Type = "Quote",
                        QuoteId = quote.Id,
                    };

                    break;
                default:
                    break;
            }
            _context.Notifications.Add(quoteNotification);
            quote.Status = status;
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
