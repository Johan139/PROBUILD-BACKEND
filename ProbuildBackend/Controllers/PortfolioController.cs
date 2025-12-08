using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PortfolioController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public PortfolioController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetPortfolio(string userId)
        {
            var portfolio = await _context
                .Portfolios.Include(p => p.Jobs)
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (portfolio == null)
            {
                return NotFound();
            }

            return Ok(portfolio);
        }

        [HttpPost("{userId}/add/{jobId}")]
        public async Task<IActionResult> AddJobToPortfolio(string userId, int jobId)
        {
            var portfolio = await _context
                .Portfolios.Include(p => p.Jobs)
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (portfolio == null)
            {
                portfolio = new Portfolio { UserId = userId, Jobs = new List<JobModel>() };
                _context.Portfolios.Add(portfolio);
            }

            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                return NotFound("Job not found.");
            }

            portfolio.Jobs.Add(job);
            await _context.SaveChangesAsync();

            return Ok(portfolio);
        }

        [HttpDelete("{userId}/remove/{jobId}")]
        public async Task<IActionResult> RemoveJobFromPortfolio(string userId, int jobId)
        {
            var portfolio = await _context
                .Portfolios.Include(p => p.Jobs)
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (portfolio == null)
            {
                return NotFound("Portfolio not found.");
            }

            var job = portfolio.Jobs.FirstOrDefault(j => j.Id == jobId);
            if (job == null)
            {
                return NotFound("Job not found in portfolio.");
            }

            portfolio.Jobs.Remove(job);
            await _context.SaveChangesAsync();

            return Ok(portfolio);
        }
    }
}
