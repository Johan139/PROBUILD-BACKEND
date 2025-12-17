using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RatingsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserModerationService _userModerationService;

        public RatingsController(
            ApplicationDbContext context,
            UserModerationService userModerationService
        )
        {
            _context = context;
            _userModerationService = userModerationService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateRating([FromBody] RatingDto ratingDto)
        {
            var rating = new Rating
            {
                JobId = ratingDto.JobId,
                RatedUserId = ratingDto.RatedUserId,
                ReviewerId = User.Identity.Name, // TODO: Check if the reviewer is the logged-in user? May need better logic
                RatingValue = ratingDto.RatingValue,
                ReviewText = ratingDto.ReviewText,
                CreatedAt = DateTime.UtcNow,
            };

            _context.Ratings.Add(rating);
            await _context.SaveChangesAsync();

            await _userModerationService.CheckUserRatingsAndReports(rating.RatedUserId);

            return CreatedAtAction(
                nameof(GetRatingsForUser),
                new { userId = rating.RatedUserId },
                rating
            );
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetRatingsForUser(string userId)
        {
            var ratings = await _context
                .Ratings.Where(r => r.RatedUserId == userId)
                .Select(r => new RatingDto
                {
                    JobId = r.JobId,
                    RatedUserId = r.RatedUserId,
                    RatingValue = r.RatingValue,
                    ReviewText = r.ReviewText,
                })
                .ToListAsync();

            return Ok(ratings);
        }
    }
}
