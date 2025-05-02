using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class QuotesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public QuotesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("GetQuotes/{id}")]
        public async Task<ActionResult<IEnumerable<Quote>>> GetQuotes(string id)
        {
            try
            {
                var quotes = await _context.Quotes.Where(a => a.CreatedBy == id).ToListAsync();
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
    }
}