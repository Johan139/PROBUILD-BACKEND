using Microsoft.EntityFrameworkCore;

namespace ProbuildBackend.Services
{
    public class UserModerationService
    {
        private readonly ApplicationDbContext _context;

        public UserModerationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task CheckUserRatingsAndReports(string userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return;
            }

            if (user.IsTimedOut)
            {
                user.IsActive = false;
                _context.Update(user);
                await _context.SaveChangesAsync();
                return;
            }

            var oneStarRatings = await _context.Ratings
                .Where(r => r.RatedUserId == userId && r.RatingValue == 1)
                .OrderByDescending(r => r.CreatedAt)
                .Take(3)
                .ToListAsync();

            if (oneStarRatings.Count == 3)
            {
                user.IsTimedOut = true;
                _context.Update(user);
                await _context.SaveChangesAsync();
                return;
            }

            var reports = await _context.Reports
                .Where(r => r.ReportedUserId == userId && r.CreatedAt > DateTime.UtcNow.AddMonths(-1))
                .ToListAsync();

            if (reports.Count >= 3)
            {
                user.IsTimedOut = true;
                _context.Update(user);
                await _context.SaveChangesAsync();
            }
        }
    }
}