using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TradePackagesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IAiAnalysisService _aiAnalysisService;

        public TradePackagesController(
            ApplicationDbContext context,
            IAiAnalysisService aiAnalysisService
        )
        {
            _context = context;
            _aiAnalysisService = aiAnalysisService;
        }

        [HttpGet("{jobId}")]
        public async Task<ActionResult<IEnumerable<TradePackage>>> GetTradePackages(int jobId)
        {
            return await _context.TradePackages.Where(tp => tp.JobId == jobId).ToListAsync();
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTradePackage(int id, TradePackage tradePackage)
        {
            if (id != tradePackage.Id)
            {
                return BadRequest();
            }

            _context.Entry(tradePackage).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.TradePackages.Any(e => e.Id == id))
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

        [HttpPost("{id}/post")]
        public async Task<IActionResult> PostToMarketplace(int id)
        {
            var tradePackage = await _context.TradePackages.FindAsync(id);
            if (tradePackage == null)
            {
                return NotFound();
            }

            tradePackage.PostedToMarketplace = true;
            tradePackage.Status = "Posted";
            await _context.SaveChangesAsync();

            return Ok(new { message = "Package posted to marketplace" });
        }

        [HttpPost("{jobId}/refresh")]
        public async Task<IActionResult> RefreshTradePackages(int jobId)
        {
            try
            {
                await _aiAnalysisService.RefreshTradePackagesAsync(jobId);
                return Ok(new { message = "Trade packages refreshed successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new { error = "Failed to refresh trade packages", details = ex.Message }
                );
            }
        }
    }
}
