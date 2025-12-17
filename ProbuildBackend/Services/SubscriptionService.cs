using ProbuildBackend.Models;

namespace ProbuildBackend.Services
{
    public class SubscriptionService
    {
        private readonly ApplicationDbContext _context;

        public SubscriptionService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<bool> CanSubmitQuote(string userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                throw new Exception("User not found");
            }

            await ResetQuoteCountIfNeeded(user);

            var limits = GetSubscriptionLimits(user.SubscriptionPackage);
            return user.QuoteCount < limits.quoteLimit;
        }

        public async Task IncrementQuoteCount(string userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                throw new Exception("User not found");
            }

            if (await CanSubmitQuote(userId))
            {
                user.QuoteCount++;
                await _context.SaveChangesAsync();
            }
            else
            {
                throw new Exception("Quote submission limit reached.");
            }
        }

        private async Task ResetQuoteCountIfNeeded(UserModel user)
        {
            var limits = GetSubscriptionLimits(user.SubscriptionPackage);
            var now = DateTime.UtcNow;

            if (now >= user.LastQuoteReset.AddDays(limits.refreshCycleDays))
            {
                user.QuoteCount = 0;
                user.LastQuoteReset = now;
                user.QuoteRefreshRound = (user.QuoteRefreshRound % 5) + 1; // Cycle through 1, 2, 3, 4, 5
                await _context.SaveChangesAsync();
            }
        }

        private (int quoteLimit, int refreshCycleDays) GetSubscriptionLimits(
            string subscriptionPackage
        )
        {
            return subscriptionPackage switch
            {
                "Essential" => (10, 30), // Tier 1
                "Growth" => (25, 30), // Tier 2
                "Pro" => (50, 30), // Tier 3
                _ => (5, 30), // Default for free/other tiers
            };
        }

        public async Task GrantTemporaryTier1Access(string userId, string jobId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                throw new Exception("User not found");
            }

            if (
                user.SubscriptionPackage == "Basic (Free) ($0.00)"
                || string.IsNullOrEmpty(user.SubscriptionPackage)
                || user.SubscriptionPackage == "BASIC"
                || user.SubscriptionPackage == "Basic"
                || user.SubscriptionPackage == "Trial Version (3 Days)"
                || user.SubscriptionPackage == "Trial Version (7 Days)"
            )
            {
                // TODO: This logic should be updated to determine the end date based on the subtasks assigned to the specific user, not the job as a whole
                // For now, we are using the latest end date from all subtasks for the job
                var endDate = _context
                    .JobSubtasks.Where(s => s.JobId.ToString() == jobId)
                    .OrderByDescending(s => s.EndDate)
                    .Select(s => s.EndDate)
                    .FirstOrDefault();

                if (endDate == default(DateTime))
                {
                    endDate = DateTime.UtcNow.AddMonths(1); // Default to 1 month if no subtasks are found
                }

                var temporaryAccess = new TempSubscriptionAccess
                {
                    UserId = userId,
                    JobId = jobId,
                    StartDate = DateTime.UtcNow,
                    EndDate = endDate,
                    OriginalSubscriptionTier = user.SubscriptionPackage,
                    IsActive = true,
                };

                _context.TempSubscriptionAccess.Add(temporaryAccess);

                user.SubscriptionPackage = "Essential";
                await _context.SaveChangesAsync();
            }
        }
    }
}
