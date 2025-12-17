using System.Security.Claims;
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

        [HttpGet]
        public async Task<ActionResult<IEnumerable<BidModel>>> GetBids()
        {
            return await _context.Bids.Include(b => b.Job).Include(b => b.User).ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<BidModel>> GetBid(int id)
        {
            var bid = await _context
                .Bids.Include(b => b.Job)
                .Include(b => b.User)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bid == null)
            {
                return NotFound();
            }

            return bid;
        }

        [HttpGet("job/{jobId}")]
        public async Task<ActionResult<IEnumerable<BidModel>>> GetBidsForJob(int jobId)
        {
            return await _context
                .Bids.Where(b => b.JobId == jobId)
                .Include(b => b.User)
                .ToListAsync();
        }

        [HttpPost("upload")]
        public async Task<ActionResult<BidModel>> PostPdfBid([FromBody] PdfBidDto bidRequest)
        {
            var userId = User.FindFirstValue("UserId");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var bid = new BidModel
            {
                JobId = bidRequest.JobId,
                DocumentUrl = bidRequest.DocumentUrl,
                UserId = userId,
                Status = "Submitted",
                SubmittedAt = DateTime.UtcNow,
            };

            _context.Bids.Add(bid);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetBid), new { id = bid.Id }, bid);
        }

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

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBid(int id)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var bid = await _context.Bids.FindAsync(id);
            if (bid == null)
            {
                return NotFound();
            }

            if (bid.UserId != userId)
            {
                return Forbid();
            }

            _context.Bids.Remove(bid);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("{id}/withdraw")]
        public async Task<IActionResult> WithdrawBid(int id)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var bid = await _context.Bids.FindAsync(id);
            if (bid == null)
            {
                return NotFound();
            }

            if (bid.UserId != userId)
            {
                return Forbid();
            }

            if (bid.Status != "Submitted")
            {
                return BadRequest("Only submitted bids can be withdrawn.");
            }

            bid.Status = "Withdrawn";
            await _context.SaveChangesAsync();

            return Ok(bid);
        }

        private bool BidExists(int id)
        {
            return _context.Bids.Any(e => e.Id == id);
        }
    }
}
