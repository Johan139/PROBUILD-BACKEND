using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Geom;
using iText.Kernel.Colors;
using iText.Layout.Borders;
using iText.IO.Image;
using iText.Kernel.Font;
using iText.IO.Font.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    IEmailTemplateService emailTemplate)
        {
            _context = context;
            _subscriptionService = subscriptionService;
            _azureBlobService = azureBlobService;
            _logger = logger;
            _emailSender = emailSender;
            _emailTemplate = emailTemplate;
        }

        // ======================================================
        // SAVE DRAFT (CREATE OR NEW VERSION)
        // ======================================================
        [HttpPost("draft")]
        public async Task<IActionResult> SaveDraft([FromBody] QuoteDto dto)
        {
            if (dto == null) return BadRequest();

            try
            {
                Quote quote;

                if (dto.QuoteId == null)
                {
                    // Generate sequential number
                    var lastQuote = await _context.Quotes
                        .Where(q => q.DocumentType == dto.DocumentType)
                        .OrderByDescending(q => q.CreatedDate)
                        .FirstOrDefaultAsync();

                    string generatedNumber;
                    if (lastQuote != null && !string.IsNullOrEmpty(lastQuote.Number))
                    {
                        var parts = lastQuote.Number.Split('-');
                        if (parts.Length == 2 && int.TryParse(parts[1], out int lastNum))
                        {
                            var prefix = dto.DocumentType == "QUOTE" ? "Q" : "INV";
                            generatedNumber = $"{prefix}-{(lastNum + 1):D3}";
                        }
                        else
                        {
                            generatedNumber = dto.DocumentType == "QUOTE" ? "Q-001" : "INV-001";
                        }
                    }
                    else
                    {
                        generatedNumber = dto.DocumentType == "QUOTE" ? "Q-001" : "INV-001";
                    }

                    quote = new Quote
                    {
                        Id = Guid.NewGuid(),
                        JobID = dto.JobID,
                        Number = generatedNumber,
                        DocumentType = dto.DocumentType,
                        Status = "Draft",
                        CreatedBy = dto.CreatedBy,
                        CreatedID = dto.CreatedID,
                        CurrentVersion = 1,
                        CreatedDate = DateTime.UtcNow
                    };

                    _context.Quotes.Add(quote);
                }
                else
                {
                    quote = await _context.Quotes.FindAsync(dto.QuoteId.Value);
                    if (quote == null) return NotFound();

                    if (quote.Status != "Draft")
                        return BadRequest("Cannot edit a submitted quote.");
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
                    CreatedDate = DateTime.UtcNow,
                    ClientAddress = dto.ClientAddress,
                    ClientPhone = dto.ClientPhone,
                    ClientEmail = dto.ClientEmail,
                    ProjectName = dto.ProjectName,
                    ProjectAddress = dto.ProjectAddress,
                    LogoId = dto.LogoId // ✅ Save logo reference (Guid)
                };

                _context.QuoteVersions.Add(version);

                foreach (var row in dto.Rows)
                {
                    _context.QuoteRows.Add(new QuoteRow
                    {
                        Id = Guid.NewGuid(),
                        QuoteVersionId = version.Id,
                        Description = row.Description,
                        Quantity = row.Quantity,
                        Unit = row.Unit,
                        UnitPrice = row.UnitPrice,
                        Total = row.Total
                    });
                }

                foreach (var cost in dto.ExtraCosts)
                {
                    _context.QuoteExtraCosts.Add(new QuoteExtraCost
                    {
                        Id = Guid.NewGuid(),
                        QuoteVersionId = version.Id,
                        Type = cost.Type,
                        Value = cost.Value,
                        Title = cost.Title
                    });
                }

                quote.CurrentVersion++;

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    QuoteId = quote.Id,
                    Version = version.Version
                });
            }
            catch
            {
                throw;
            }
        }

        // ======================================================
        // SUBMIT QUOTE (LOCK)
        // ======================================================
        [HttpPost("{quoteId}/submit")]
        public async Task<IActionResult> SubmitQuote(Guid quoteId)
        {
            var quote = await _context.Quotes.FindAsync(quoteId);
            if (quote == null) return NotFound();

            if (quote.Status != "Draft")
                return BadRequest("Quote already submitted.");

            var canSubmit = await _subscriptionService.CanSubmitQuote(quote.CreatedID);
            if (!canSubmit)
                return BadRequest("Quote submission limit reached.");

            quote.Status = "Submitted";

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
            if (quote == null) return NotFound();

            var versionNumber = quote.CurrentVersion - 1;

            var version = await _context.QuoteVersions
                .FirstOrDefaultAsync(v =>
                    v.QuoteId == quoteId &&
                    v.Version == versionNumber);

            if (version == null) return NotFound();

            var rows = await _context.QuoteRows
                .Where(r => r.QuoteVersionId == version.Id)
                .ToListAsync();

            var extras = await _context.QuoteExtraCosts
                .Where(e => e.QuoteVersionId == version.Id)
                .ToListAsync();

            // ✅ Load logo if exists (using Guid)
            LogosModel? logo = null;
            if (version.LogoId.HasValue)
            {
                logo = await _context.Logos.FindAsync(version.LogoId.Value);
            }

            return Ok(new QuoteViewDto
            {
                QuoteId = quote.Id,
                Number = quote.Number,
                Status = quote.Status,
                DocumentType = quote.DocumentType,
                CurrentVersion = version.Version,

                Version = new QuoteVersionDto
                {
                    Version = version.Version,
                    Header = version.Header,
                    From = version.From,
                    To = version.To,
                    Date = version.Date,
                    DueDate = version.DueDate,
                    Notes = version.Notes,
                    Terms = version.Terms,
                    Total = version.Total,
                    ClientAddress = version.ClientAddress,
                    ClientEmail = version.ClientEmail,
                    ClientPhone = version.ClientPhone,
                    ProjectAddress = version.ProjectAddress,
                    ProjectName = version.ProjectName,
                    LogoId = version.LogoId // ✅ Include logo ID (Guid)
                },

                LogoUrl = logo?.Url, // ✅ Use Url property from LogosModel

                Rows = rows.Select(r => new QuoteRowDto
                {
                    Description = r.Description,
                    Quantity = r.Quantity,
                    Unit = r.Unit,
                    UnitPrice = r.UnitPrice,
                    Total = r.Total
                }).ToList(),

                ExtraCosts = extras.Select(e => new QuoteExtraCostDto
                {
                    Type = e.Type,
                    Value = e.Value,
                    Title = e.Title
                }).ToList()
            });
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
            var quotes = await _context.Quotes
                .Where(q => q.CreatedID == userId)
                .OrderByDescending(q => q.CreatedDate)
                .Select(q => new
                {
                    id = q.Id,
                    number = q.Number,
                    status = q.Status,
                    createdDate = q.CreatedDate,
                    createdBy = q.CreatedID,
                    total = _context.QuoteVersions
                        .Where(v => v.QuoteId == q.Id && v.Version == q.CurrentVersion)
                        .Select(v => v.Total)
                        .FirstOrDefault(),

                    jobName = _context.Jobs
                        .Where(j => j.Id == q.JobID)
                        .Select(j => j.ProjectName)
                        .FirstOrDefault()
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
                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
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
                    Type = "quote-logo"
                };

                _context.Logos.Add(logo);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    id = logo.Id,
                    url = logo.Url
                });
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
            if (logo == null) return NotFound();

            return Ok(new
            {
                id = logo.Id,
                url = logo.Url,
                fileName = logo.FileName
            });
        }
        [HttpGet("logo/file/{logoId}")]
        public async Task<IActionResult> GetLogoFile(Guid logoId)
        {
            var logo = await _context.Logos.FindAsync(logoId);
            if (logo == null)
                return NotFound();

            var (stream, contentType, fileName) =
                await _azureBlobService.GetBlobContentAsync(logo.Url);

            return File(stream, contentType);
        }

        // ======================================================
        // GET USER'S DEFAULT LOGO
        // ======================================================
        [HttpGet("logo/user/{userId}")]
        public async Task<IActionResult> GetUserLogo(string userId)
        {
            var logo = await _context.Logos
                .Where(l => l.UploadedBy == userId && l.Type == "quote-logo")
                .OrderByDescending(l => l.UploadedAt)
                .FirstOrDefaultAsync();

            if (logo == null) return NotFound("No logo found for user");

            return Ok(new
            {
                id = logo.Id,
                url = logo.Url,
                fileName = logo.FileName
            });
        }
        // ======================================================
        // SEND TO CLIENT (SUBMIT + EMAIL)
        // ======================================================
        [HttpPost("{quoteId}/send-to-client")]
        public async Task<IActionResult> SendToClient(Guid quoteId, [FromBody] SendQuoteToClientDto dto)
        {
            try
            {
                // 1. Get the quote
                var quote = await _context.Quotes
                    .FirstOrDefaultAsync(q => q.Id == quoteId);

                if (quote == null)
                    return NotFound("Quote not found");

                // 2. Get the latest version for quote details
                var latestVersion = await _context.QuoteVersions
                    .Where(v => v.QuoteId == quoteId)
                    .OrderByDescending(v => v.Version)
                    .FirstOrDefaultAsync();

                if (latestVersion == null)
                    return BadRequest("Quote has no version data");

                // 3. Validate client email
                var clientEmail = !string.IsNullOrWhiteSpace(dto.ClientEmail)
                    ? dto.ClientEmail
                    : latestVersion.ClientEmail;

                if (string.IsNullOrWhiteSpace(clientEmail))
                    return BadRequest("Client email is required");

                // 4. Check subscription limits (only if not already submitted)
                if (quote.Status == "Draft")
                {
                    var canSubmit = await _subscriptionService.CanSubmitQuote(quote.CreatedID);
                    if (!canSubmit)
                        return BadRequest("Quote submission limit reached for your subscription");
                }

                // 5. Get job details
                string projectName = latestVersion.ProjectName ?? "Your Project";
                if (quote.JobID.HasValue)
                {
                    var job = await _context.Jobs.FindAsync(quote.JobID.Value);
                    if (job != null && !string.IsNullOrEmpty(job.ProjectName))
                    {
                        projectName = job.ProjectName;
                    }
                }

                // 6. Get client name
                var clientName = !string.IsNullOrWhiteSpace(dto.ClientName)
                    ? dto.ClientName
                    : (!string.IsNullOrWhiteSpace(latestVersion.To) ? latestVersion.To : "Valued Customer");

                // 7. Build the quote link
                var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "https://app.probuildai.com";
                var quoteLink = $"{frontendUrl}/quote?quoteId={quote.Id}";

                // 8. Get and populate the email template (your existing template)
                var emailTemplate = await _emailTemplate.GetTemplateAsync("NewQuoteSubmittedEmail");

                emailTemplate.Subject = emailTemplate.Subject
                    .Replace("{{job.ProjectName}}", projectName);

                emailTemplate.Body = emailTemplate.Body
                    .Replace("{{Header}}", emailTemplate.HeaderHtml)
                    .Replace("{{Footer}}", emailTemplate.FooterHtml)
                    .Replace("{{UserName}}", clientName)
                    .Replace("{{quote.Number}}", quote.Number)
                    .Replace("{{job.ProjectName}}", projectName)
                    .Replace("{{QuoteLink}}", quoteLink);

                // 9. Generate PDF
                var pdfBytes = await GenerateQuotePdf(quote, latestVersion, quoteId);

                var docType = quote.DocumentType == "INVOICE" ? "Invoice" : "Quote";
                var pdfFileName = $"{docType}_{quote.Number}.pdf";

                // 10. Send the email with PDF attachment
                await _emailSender.SendEmailWithAttachmentAsync(
                    emailTemplate,
                    clientEmail,
                    pdfBytes,
                    pdfFileName,
                    "application/pdf"
                );

                // 11. Update quote status (only if it was a draft)
                if (quote.Status == "Draft")
                {
                    quote.Status = "Submitted";
                    await _subscriptionService.IncrementQuoteCount(quote.CreatedID);
                }

                // 12. Save changes
                await _context.SaveChangesAsync();

                _logger.LogInformation("Quote {QuoteId} sent to client {Email} with PDF attachment", quoteId, clientEmail);

                return Ok(new
                {
                    Success = true,
                    Message = $"{docType} successfully sent to {clientEmail}",
                    QuoteId = quoteId,
                    Status = quote.Status
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending quote {QuoteId} to client", quoteId);
                return StatusCode(500, $"Failed to send quote: {ex.Message}");
            }
        }
        private async Task<byte[]> GenerateQuotePdf(Quote quote, QuoteVersionModel version, Guid quoteId)
        {
            // Get quote rows
            var rows = await _context.QuoteRows
                .Where(r => r.QuoteVersionId == version.Id)
                .ToListAsync();

            // Get extra costs
            var extraCosts = await _context.QuoteExtraCosts
                .Where(e => e.QuoteVersionId == version.Id)
                .ToListAsync();

            // Get logo if exists
            string? logoUrl = null;
            if (version.LogoId.HasValue)
            {
                var logo = await _context.Logos.FindAsync(version.LogoId.Value);
                logoUrl = logo?.Url;
            }

            var docType = quote.DocumentType == "INVOICE" ? "INVOICE" : "QUOTE";

            // Create the PDF in memory
            var ms = new MemoryStream();
            var writer = new PdfWriter(ms);
            var pdf = new PdfDocument(writer);
            var document = new Document(pdf, PageSize.A4);

            document.SetMargins(40, 40, 40, 40);

            // Define fonts
            var boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var regularFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            // Define colors
            var primaryYellow = new DeviceRgb(251, 208, 8);
            var darkGray = new DeviceRgb(51, 51, 51);
            var lightGray = new DeviceRgb(245, 245, 245);
            var black = new DeviceRgb(0, 0, 0);

            // === HEADER SECTION ===
            var headerTable = new Table(2).UseAllAvailableWidth();

            // Left: Logo + Company Info
            var leftCell = new Cell().SetBorder(Border.NO_BORDER);

            // Add logo if exists
            if (!string.IsNullOrEmpty(logoUrl))
            {
                try
                {
                    var (logoStream, _, _) = await _azureBlobService.GetBlobContentAsync(logoUrl);
                    var logoMs = new MemoryStream();
                    await logoStream.CopyToAsync(logoMs);
                    var logoImage = new Image(ImageDataFactory.Create(logoMs.ToArray()))
                        .SetWidth(80)
                        .SetMarginBottom(10);
                    leftCell.Add(logoImage);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to load logo for PDF: {Error}", ex.Message);
                }
            }

            leftCell.Add(new Paragraph(version.From ?? "")
                .SetFont(boldFont)
                .SetFontSize(14)
                .SetFontColor(black));

            if (!string.IsNullOrEmpty(version.ClientAddress))
                leftCell.Add(new Paragraph(version.ClientAddress)
                    .SetFont(regularFont)
                    .SetFontSize(10)
                    .SetFontColor(darkGray));

            headerTable.AddCell(leftCell);

            // Right: Quote Title + Meta
            var rightCell = new Cell().SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT);

            rightCell.Add(new Paragraph(docType)
                .SetFont(boldFont)
                .SetFontSize(28)
                .SetFontColor(black));

            rightCell.Add(new Paragraph($"{docType} #: {quote.Number}")
                .SetFont(regularFont)
                .SetFontSize(10)
                .SetFontColor(darkGray));

            rightCell.Add(new Paragraph($"Date: {version.Date?.ToString("yyyy-MM-dd") ?? "N/A"}")
                .SetFont(regularFont)
                .SetFontSize(10)
                .SetFontColor(darkGray));

            rightCell.Add(new Paragraph($"Valid Until: {version.DueDate?.ToString("yyyy-MM-dd") ?? "N/A"}")
                .SetFont(regularFont)
                .SetFontSize(10)
                .SetFontColor(darkGray));

            headerTable.AddCell(rightCell);
            document.Add(headerTable);

            // Divider
            document.Add(new Paragraph("")
                .SetMarginTop(10)
                .SetMarginBottom(10)
                .SetBorderBottom(new SolidBorder(primaryYellow, 2)));

            // === BILL TO / PROJECT SECTION ===
            var detailsTable = new Table(2).UseAllAvailableWidth().SetMarginTop(10);

            // Bill To
            var billToCell = new Cell().SetBorder(Border.NO_BORDER);
            billToCell.Add(new Paragraph("Bill To:")
                .SetFont(boldFont)
                .SetFontSize(12));
            billToCell.Add(new Paragraph(version.To ?? "")
                .SetFont(regularFont)
                .SetFontSize(10));
            if (!string.IsNullOrEmpty(version.ClientAddress))
                billToCell.Add(new Paragraph(version.ClientAddress)
                    .SetFont(regularFont)
                    .SetFontSize(10));
            if (!string.IsNullOrEmpty(version.ClientPhone))
                billToCell.Add(new Paragraph(version.ClientPhone)
                    .SetFont(regularFont)
                    .SetFontSize(10));
            if (!string.IsNullOrEmpty(version.ClientEmail))
                billToCell.Add(new Paragraph(version.ClientEmail)
                    .SetFont(regularFont)
                    .SetFontSize(10));
            detailsTable.AddCell(billToCell);

            // Project
            var projectCell = new Cell().SetBorder(Border.NO_BORDER);
            projectCell.Add(new Paragraph("Project:")
                .SetFont(boldFont)
                .SetFontSize(12));
            projectCell.Add(new Paragraph(version.ProjectName ?? "")
                .SetFont(regularFont)
                .SetFontSize(10));
            if (!string.IsNullOrEmpty(version.ProjectAddress))
                projectCell.Add(new Paragraph(version.ProjectAddress)
                    .SetFont(regularFont)
                    .SetFontSize(10));
            detailsTable.AddCell(projectCell);

            document.Add(detailsTable);

            // === LINE ITEMS TABLE ===
            document.Add(new Paragraph("Line Items")
                .SetFont(boldFont)
                .SetFontSize(14)
                .SetMarginTop(20));

            var itemsTable = new Table(new float[] { 4, 1, 1, 1.5f, 1.5f }).UseAllAvailableWidth().SetMarginTop(10);

            // Header row
            var headers = new[] { "Description", "Qty", "Unit", "Unit Price", "Amount" };
            foreach (var header in headers)
            {
                itemsTable.AddHeaderCell(new Cell()
                    .Add(new Paragraph(header).SetFont(boldFont).SetFontSize(10))
                    .SetBackgroundColor(lightGray)
                    .SetPadding(8));
            }

            // Data rows
            foreach (var row in rows)
            {
                itemsTable.AddCell(new Cell()
                    .Add(new Paragraph(row.Description ?? "").SetFont(regularFont).SetFontSize(10))
                    .SetPadding(8));
                itemsTable.AddCell(new Cell()
                    .Add(new Paragraph(row.Quantity.ToString()).SetFont(regularFont).SetFontSize(10))
                    .SetPadding(8)
                    .SetTextAlignment(TextAlignment.CENTER));
                itemsTable.AddCell(new Cell()
                    .Add(new Paragraph(row.Unit ?? "").SetFont(regularFont).SetFontSize(10))
                    .SetPadding(8)
                    .SetTextAlignment(TextAlignment.CENTER));
                itemsTable.AddCell(new Cell()
                    .Add(new Paragraph($"${row.UnitPrice:N2}").SetFont(regularFont).SetFontSize(10))
                    .SetPadding(8)
                    .SetTextAlignment(TextAlignment.RIGHT));
                itemsTable.AddCell(new Cell()
                    .Add(new Paragraph($"${row.Total:N2}").SetFont(regularFont).SetFontSize(10))
                    .SetPadding(8)
                    .SetTextAlignment(TextAlignment.RIGHT));
            }

            document.Add(itemsTable);

            // === TOTALS SECTION ===
            var totalsTable = new Table(new float[] { 4, 2 }).UseAllAvailableWidth().SetMarginTop(10);
            totalsTable.SetHorizontalAlignment(HorizontalAlignment.RIGHT);

            // Subtotal
            var subtotal = rows.Sum(r => r.Total);
            totalsTable.AddCell(new Cell()
                .Add(new Paragraph("Subtotal:").SetFont(boldFont))
                .SetBorder(Border.NO_BORDER)
                .SetTextAlignment(TextAlignment.RIGHT)
                .SetPadding(5));
            totalsTable.AddCell(new Cell()
                .Add(new Paragraph($"${subtotal:N2}").SetFont(regularFont))
                .SetBorder(Border.NO_BORDER)
                .SetTextAlignment(TextAlignment.RIGHT)
                .SetPadding(5));

            // Extra costs
            foreach (var cost in extraCosts)
            {
                var label = cost.Type == "Discount" ? $"Discount ({cost.Value}%):" : $"{cost.Title ?? cost.Type}:";
                var value = cost.Type == "Discount" || cost.Type == "Tax"
                    ? $"{cost.Value}%"
                    : $"${cost.Value:N2}";

                totalsTable.AddCell(new Cell()
                    .Add(new Paragraph(label).SetFont(regularFont))
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.RIGHT)
                    .SetPadding(5));
                totalsTable.AddCell(new Cell()
                    .Add(new Paragraph(value).SetFont(regularFont))
                    .SetBorder(Border.NO_BORDER)
                    .SetTextAlignment(TextAlignment.RIGHT)
                    .SetPadding(5));
            }

            // Grand Total
            totalsTable.AddCell(new Cell()
                .Add(new Paragraph("Total:").SetFont(boldFont).SetFontSize(14))
                .SetBackgroundColor(primaryYellow)
                .SetBorder(Border.NO_BORDER)
                .SetTextAlignment(TextAlignment.RIGHT)
                .SetPadding(10));
            totalsTable.AddCell(new Cell()
                .Add(new Paragraph($"${version.Total:N2}").SetFont(boldFont).SetFontSize(14))
                .SetBackgroundColor(primaryYellow)
                .SetBorder(Border.NO_BORDER)
                .SetTextAlignment(TextAlignment.RIGHT)
                .SetPadding(10));

            document.Add(totalsTable);

            // === NOTES & TERMS ===
            if (!string.IsNullOrEmpty(version.Notes))
            {
                document.Add(new Paragraph("Notes")
                    .SetFont(boldFont)
                    .SetFontSize(12)
                    .SetMarginTop(20));
                document.Add(new Paragraph(version.Notes)
                    .SetFont(regularFont)
                    .SetFontSize(10)
                    .SetFontColor(darkGray));
            }

            if (!string.IsNullOrEmpty(version.Terms))
            {
                document.Add(new Paragraph("Terms & Conditions")
                    .SetFont(boldFont)
                    .SetFontSize(12)
                    .SetMarginTop(15));
                document.Add(new Paragraph(version.Terms)
                    .SetFont(regularFont)
                    .SetFontSize(10)
                    .SetFontColor(darkGray));
            }

            // === FOOTER ===
            document.Add(new Paragraph("Generated by ProBuildAI")
                .SetFont(regularFont)
                .SetFontSize(9)
                .SetFontColor(new DeviceRgb(150, 150, 150))
                .SetTextAlignment(TextAlignment.CENTER)
                .SetMarginTop(30));

            // Close document to flush content to stream
            document.Close();

            // Return the bytes
            return ms.ToArray();
        }

        // ======================================================
        // CHANGE STATUS (APPROVE / REJECT)
        // ======================================================
        [HttpPost("{quoteId}/status")]
        public async Task<IActionResult> ChangeStatus(
            Guid quoteId,
            [FromBody] string status)
        {
            var quote = await _context.Quotes.FindAsync(quoteId);
            if (quote == null) return NotFound();

            quote.Status = status;
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}