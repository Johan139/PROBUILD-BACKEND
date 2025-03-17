using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BidsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public BidsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/Bids
        [HttpGet]
        public async Task<ActionResult<IEnumerable<BidModel>>> GetBids()
        {
            return await _context.Bids
                .Include(b => b.Job) // Include related Job
                .Include(b => b.User)    // Include related user
                .ToListAsync();
        }

        // GET: api/Bids/5
        [HttpGet("{id}")]
        public async Task<ActionResult<BidModel>> GetBid(int id)
        {
            var bid = await _context.Bids
                .Include(b => b.Job)
                .Include(b => b.User)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bid == null)
            {
                return NotFound();
            }

            return bid;
        }

        // POST: api/Bids
        [HttpPost]
        public async Task<ActionResult<BidModel>> PostBid([FromForm] BidDto bidrequest)
        {
            var bid = new BidModel
            {
                Task = bidrequest.Task,
                Duration = bidrequest.Duration,
                JobId = bidrequest.JobId,
                UserId = bidrequest.UserId
            };

            if (bidrequest.Quote != null)
            {
                // Read the quote file into a byte array
                using (var memoryStream = new MemoryStream())
                {
                    await bidrequest.Quote.CopyToAsync(memoryStream);
                    bid.Quote = memoryStream.ToArray();
                }
            }
            else
            {
                bid.Quote = null;
            }

            _context.Bids.Add(bid);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetBid), new { id = bid.Id }, bid);
        }

        // PUT: api/Bids/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutBid(int id, BidModel bid)
        {
            if (id != bid.Id)
            {
                return BadRequest();
            }

            _context.Entry(bid).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!BidExists(id))
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

        // DELETE: api/Bids/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBid(int id)
        {
            var bid = await _context.Bids.FindAsync(id);
            if (bid == null)
            {
                return NotFound();
            }

            _context.Bids.Remove(bid);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool BidExists(int id)
        {
            return _context.Bids.Any(e => e.Id == id);
        }
    }
}
