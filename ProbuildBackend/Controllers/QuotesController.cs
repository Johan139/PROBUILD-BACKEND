using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Services;
using System.Net.Mail;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QuotesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailSender emailSender;
        private readonly IEmailSender _emailSender;

        public QuotesController(ApplicationDbContext context, IEmailSender emailSender)
        {
            _context = context;
            _emailSender = emailSender;
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

            quote.Status = newStatus;

            try
            {
                await _context.SaveChangesAsync();

                string subject;
                string message;

                switch (quote.Status)
                {
                    case "Approved":
                    case "Rejected":
                        var quoteUser = await _context.Users.FirstOrDefaultAsync(a => a.Id == quote.CreatedID);
                        if (quoteUser == null)
                            return NotFound("Quote creator not found.");
                        if (string.IsNullOrEmpty(quoteUser.Email))
                            return BadRequest("Quote creator email is missing.");

                        subject = $"Your quote #{quote.Number} has been {quote.Status.ToLower()}";
                        message = quote.Status == "Approved"
                            ? $"<p>Great news! Your quote <strong>#{quote.Number}</strong> has been approved.</p>"
                            : $"<p>Unfortunately, your quote <strong>#{quote.Number}</strong> has been rejected.</p>";

                        message += "<p>Thank you,<br/>ProBuildAI</p>";

                        await _emailSender.SendEmailAsync(quoteUser.Email, subject, message);
                        break;

                    case "Submitted":
                        var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == quote.JobID);
                        if (job == null)
                            return NotFound("Linked job not found.");

                        var jobCreator = await _context.Users.FirstOrDefaultAsync(u => u.Id == job.UserId);
                        if (jobCreator == null || string.IsNullOrEmpty(jobCreator.Email))
                            return BadRequest("Job creator not found or email is missing.");

                        subject = $"Quote submitted for job '{job.ProjectName}'";
                        message = $@"
                            <p>A new quote <strong>#{quote.Number}</strong> has been <strong>submitted</strong> 
                            for the job <strong>{job.ProjectName}</strong>.</p>
                            <p>Please log in to review and take action.</p>
                            <p>Thanks,<br/>ProBuildAI</p>";

                        await _emailSender.SendEmailAsync(jobCreator.Email, subject, message);
                        break;
                }

                return Ok(quote);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error updating status: {ex.Message}");
            }
        }

    }
}
