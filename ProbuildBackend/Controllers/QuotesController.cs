using Microsoft.AspNetCore.Identity.UI.Services;
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
    [Route("api/[controller]")]
    public class QuotesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailSender emailSender;
        private readonly IEmailSender _emailSender;
        private readonly SubscriptionService _subscriptionService;
        public readonly IEmailTemplateService _emailTemplate;
        private readonly IConfiguration _configuration;
        private readonly AzureBlobService _azureBlobService;
        public QuotesController(ApplicationDbContext context, IEmailSender emailSender, SubscriptionService subscriptionService, IEmailTemplateService emailTemplate, IConfiguration configuration, AzureBlobService azureBlobService)
        {
            _context = context;
            _emailSender = emailSender;
            _subscriptionService = subscriptionService;
            _emailTemplate = emailTemplate;
            _configuration = configuration;
            _azureBlobService = azureBlobService;
        }

        [HttpGet("GetQuotes/{id}")]
        public async Task<ActionResult<IEnumerable<Quote>>> GetQuotes(string id)
        {
            try
            {
                var quotes = await _context.Quotes
                    .Where(q => q.CreatedID == id)
                    .GroupBy(q => q.Number)
                    .Select(g => g.OrderByDescending(q => q.Version).First())
                    .ToListAsync();
                return Ok(quotes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving quotes: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost]
        public async Task<ActionResult<Quote>> PostQuote(Quote quote)
        {
            if (quote == null)
            {
                return BadRequest("Quote cannot be null.");
            }

            if (string.IsNullOrEmpty(quote.Id))
            {
                quote.Id = Guid.NewGuid().ToString();
            }

            foreach (var row in quote.Rows ?? new List<QuoteRow>())
            {
                row.QuoteId = quote.Id;
            }
            foreach (var extraCost in quote.ExtraCosts ?? new List<QuoteExtraCost>())
            {
                extraCost.QuoteId = quote.Id;
            }

            _context.Quotes.Add(quote);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetQuote), new { id = quote.Id }, quote);
        }

        [HttpGet("GetQuote/{id}")]
        public async Task<ActionResult<Quote>> GetQuote(string id)
        {
            try
            {
                var quote = await _context.Quotes
                    .FirstOrDefaultAsync(q => q.Id == id);
                if (quote == null)
                    return NotFound();

                var quoteRow = await _context.QuoteRows.Where(q => q.QuoteId == id).ToListAsync();
                if (quoteRow == null)
                    return NotFound();

                return Ok(quote);
            }
            catch (Exception e)
            {
                return BadRequest(e);
            }
        }

        [HttpGet("GetJobQuotes/{jobId}")]
        public async Task<ActionResult<List<Quote>>> GetJobQuotes(int jobId)
        {
            try
            {
                var quote = await _context.Quotes
                    .Where(q => q.JobID == jobId).ToListAsync();
                if (quote == null)
                    return NotFound();

                return Ok(quote);
            }
            catch (Exception e)
            {
                return BadRequest(e);
            }
        }

        [HttpGet("GetJobQuotesByUserId/{userId}")]
        public async Task<ActionResult<List<Quote>>> GetJobQuotesByUserId(string userId)
        {
            try
            {
                List<Quote> quoteList = new List<Quote>();
                var jobList = await _context.Jobs
                    .Where(q => q.UserId == userId).ToListAsync();
                if (jobList == null)
                    return NotFound();

                foreach (var job in jobList)
                {
                    var quote = await _context.Quotes
                        .Where(q => q.JobID == job.Id && q.Status == "Submitted")
                        .GroupBy(q => q.Number)
                        .Select(g => g.OrderByDescending(q => q.Version).First())
                        .ToListAsync();
                    if (quote.Count == 0)
                        continue;
                    quoteList.AddRange(quote);
                }

                return Ok(quoteList);
            }
            catch (Exception e)
            {
                return BadRequest(e);
            }
        }

        [HttpPost("UpdateQuote/{id}")]
        public async Task<ActionResult<Quote>> UpdateQuote(string id, Quote quote)
        {
            if (quote == null || quote.Id != id)
            {
                return BadRequest("Invalid quote data or ID mismatch.");
            }

            var existingQuote = await _context.Quotes
                .Include(q => q.Rows)
                .Include(q => q.ExtraCosts)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (existingQuote == null)
            {
                return NotFound();
            }

            // Update quote properties
            _context.Entry(existingQuote).CurrentValues.SetValues(quote);

            // Update rows
            existingQuote.Rows.Clear();
            foreach (var row in quote.Rows ?? new List<QuoteRow>())
            {
                row.QuoteId = id;
                existingQuote.Rows.Add(row);
            }

            // Update extra costs
            existingQuote.ExtraCosts.Clear();
            foreach (var extraCost in quote.ExtraCosts ?? new List<QuoteExtraCost>())
            {
                extraCost.QuoteId = id;
                existingQuote.ExtraCosts.Add(extraCost);
            }

            try
            {
                await _context.SaveChangesAsync();
                return Ok(existingQuote);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating quote: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("SaveQuoteWithVersion")]
        public async Task<ActionResult<Quote>> SaveQuoteWithVersion(Quote quote)
        {
            if (quote == null)
            {
                return BadRequest("Quote cannot be null.");
            }

            //int nextVersion = 1;
            //
            //if (quote.JobID != null)
            //{
            //    var existingQuotes = await _context.Quotes
            //        .Where(q => q.JobID == quote.JobID)
            //        .ToListAsync();
            //
            //    if (existingQuotes.Any())
            //    {
            //        nextVersion = existingQuotes.Max(q => (int?)(q.Version) ?? 1) + 1;
            //    }
            //}

            quote.Version = quote.Version + 1;
            quote.Status = "Draft";

            if (string.IsNullOrEmpty(quote.Id))
            {
                quote.Id = Guid.NewGuid().ToString();
            }

            foreach (var row in quote.Rows ?? new List<QuoteRow>())
            {
                row.QuoteId = quote.Id;
            }

            foreach (var extraCost in quote.ExtraCosts ?? new List<QuoteExtraCost>())
            {
                extraCost.QuoteId = quote.Id;
            }

            _context.Quotes.Add(quote);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetQuote), new { id = quote.Id }, quote);
        }

        [HttpPost("SubmitQuote")]
        public async Task<ActionResult<Quote>> SubmitQuote(Quote quote)
        {
            if (quote == null)
            {
                return BadRequest("Quote cannot be null.");
            }

            var canSubmit = await _subscriptionService.CanSubmitQuote(quote.CreatedID);
            if (!canSubmit)
            {
                return BadRequest("You have reached your quote submission limit for this period.");
            }

            int nextVersion = 1;

            if (quote.JobID != null)
            {
                var existingQuotes = await _context.Quotes
                    .Where(q => q.JobID == quote.JobID)
                    .ToListAsync();

                if (existingQuotes.Any())
                {
                    nextVersion = existingQuotes.Max(q => (int?)(q.Version) ?? 1) + 1;
                }
            }

            quote.Version = nextVersion;
            quote.Status = "Draft";

            if (string.IsNullOrEmpty(quote.Id))
            {
                quote.Id = Guid.NewGuid().ToString();
            }

            foreach (var row in quote.Rows ?? new List<QuoteRow>())
            {
                row.QuoteId = quote.Id;
            }

            foreach (var extraCost in quote.ExtraCosts ?? new List<QuoteExtraCost>())
            {
                extraCost.QuoteId = quote.Id;
            }

            _context.Quotes.Add(quote);
            await _context.SaveChangesAsync();

            await _subscriptionService.IncrementQuoteCount(quote.CreatedID);

            return CreatedAtAction(nameof(GetQuote), new { id = quote.Id }, quote);
        }

        [HttpPost("ChangeStatus/{id}")]
        public async Task<ActionResult> ChangeStatus(string id, [FromBody] string newStatus)
        {

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(newStatus))
                return BadRequest("Invalid ID or status.");

            var quote = await _context.Quotes.FirstOrDefaultAsync(q => q.Id == id);
            if (quote == null)
                return NotFound("Quote not found.");
            var jobName = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == quote.JobID);
            quote.Status = newStatus;

            try
            {
                await _context.SaveChangesAsync();

                string subject;
                string message;

                var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? _configuration["FrontEnd:FRONTEND_URL"];
                var callbackURL = $"{frontendUrl}/quote?quoteId=" + quote.Id;

                var quoteUser = await _context.Users.FirstOrDefaultAsync(a => a.Id == quote.CreatedID);
                switch (quote.Status)
                {
                    case "Approved":
                        quoteUser = await _context.Users.FirstOrDefaultAsync(a => a.Id == quote.CreatedID);
                        if (quoteUser == null)
                            return NotFound("Quote creator not found.");
                        if (string.IsNullOrEmpty(quoteUser.Email))
                            return BadRequest("Quote creator email is missing.");


                        var QuoteApproved = await _emailTemplate.GetTemplateAsync("QuoteApprovedEmail");

                        QuoteApproved.Subject = QuoteApproved.Subject.Replace("{{quote.Number}}", quote.Number);

                        QuoteApproved.Body = QuoteApproved.Body.Replace("{{UserName}}", quoteUser.FirstName + " " + quoteUser.LastName)
                            .Replace("{{quote.Number}}", quote.Number)
                            .Replace("{{job.ProjectName}}", jobName.ProjectName)
                            .Replace("{{QuoteLink}}", callbackURL)
                            .Replace("{{Header}}", QuoteApproved.HeaderHtml)
                .Replace("{{Footer}}", QuoteApproved.FooterHtml);

                        await _emailSender.SendEmailAsync(QuoteApproved, quoteUser.Email);
                        break;
                    case "Rejected":
                        quoteUser = await _context.Users.FirstOrDefaultAsync(a => a.Id == quote.CreatedID);
                        if (quoteUser == null)
                            return NotFound("Quote creator not found.");
                        if (string.IsNullOrEmpty(quoteUser.Email))
                            return BadRequest("Quote creator email is missing.");


                        var QuoteDeclined = await _emailTemplate.GetTemplateAsync("QuoteDeclinedEmail");

                        QuoteDeclined.Subject = QuoteDeclined.Subject.Replace("{{quote.Number}}", quote.Number);

                        QuoteDeclined.Body = QuoteDeclined.Body.Replace("{{UserName}}", quoteUser.FirstName + " " + quoteUser.LastName)
                            .Replace("{{quote.Number}}", quote.Number)
                            .Replace("{{job.ProjectName}}", jobName.ProjectName)
                            .Replace("{{QuoteLink}}", callbackURL).Replace("{{Header}}", QuoteDeclined.HeaderHtml)
                .Replace("{{Footer}}", QuoteDeclined.FooterHtml);

                        await _emailSender.SendEmailAsync(QuoteDeclined, quoteUser.Email);
                        break;

                    case "Submitted":
                        var createBidResult = await CreateBidFromQuote(quote);
                        if (createBidResult is not OkResult)
                        {
                            return StatusCode(500, "Failed to create a corresponding bid for the quote.");
                        }

                        var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == quote.JobID);
                        if (job == null)
                            return NotFound("Linked job not found.");

                        var jobCreator = await _context.Users.FirstOrDefaultAsync(u => u.Id == job.UserId);
                        if (jobCreator == null || string.IsNullOrEmpty(jobCreator.Email))
                            return BadRequest("Job creator not found or email is missing.");

                        var QuoteNew = await _emailTemplate.GetTemplateAsync("NewQuoteSubmittedEmail");

                        QuoteNew.Subject = QuoteNew.Subject.Replace("{{quote.Number}}", quote.Number);

                        QuoteNew.Body = QuoteNew.Body.Replace("{{UserName}}", jobCreator.FirstName + " " + jobCreator.LastName)
                            .Replace("{{quote.Number}}", quote.Number)
                            .Replace("{{job.ProjectName}}", job.ProjectName)
                            .Replace("{{QuoteLink}}", callbackURL).Replace("{{Header}}", QuoteNew.HeaderHtml)
                .Replace("{{Footer}}", QuoteNew.FooterHtml);

                        await _emailSender.SendEmailAsync(QuoteNew, jobCreator.Email);
                        break;
                }

                return Ok(quote);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error updating status: {ex.Message}");
            }
        }

        private async Task<ActionResult> CreateBidFromQuote(Quote quote)
        {
            var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == quote.JobID);
            if (job == null)
            {
                return NotFound("Associated job not found.");
            }

            var bid = new BidModel
            {
                JobId = quote.JobID.Value,
                UserId = quote.CreatedID,
                Amount = quote.Total,
                Status = "Submitted",
                SubmittedAt = DateTime.UtcNow,
                BiddingRound = 1,
                IsFinalist = false,
                Task = job.ProjectName,
                Duration = 0,
                DocumentUrl = null,
                QuoteId = quote.Id
            };

            _context.Bids.Add(bid);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("Upload")]
        [RequestSizeLimit(200 * 1024 * 1024)]
        public async Task<IActionResult> Upload([FromForm] UploadQuoteDto uploadQuoteDto)
        {
            if (uploadQuoteDto == null || uploadQuoteDto.Quote == null || !uploadQuoteDto.Quote.Any())
            {
                return BadRequest(new { error = "No quote file provided" });
            }

            var quoteFile = uploadQuoteDto.Quote.First();
            var allowedExtensions = new[] { ".pdf" };
            var extension = Path.GetExtension(quoteFile.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                return BadRequest(new { error = "Invalid file type. Only PDF files are allowed for quotes." });
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
                    FileName = Path.GetFileName(new Uri(fileUrl).LocalPath),
                    BlobUrl = fileUrl,
                    SessionId = uploadQuoteDto.sessionId,
                    UploadedAt = DateTime.Now
                };
                _context.JobDocuments.Add(jobDocument);
                await _context.SaveChangesAsync();
            }

            var response = new
            {
                FileUrl = fileUrl
            };

            return Ok(response);
        }
    }
}
