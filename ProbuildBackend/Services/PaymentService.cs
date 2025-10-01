using Stripe;

namespace ProbuildBackend.Services
{
    public class PaymentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly SubscriptionService _subscriptionService;

        public PaymentService(ApplicationDbContext context, IConfiguration configuration, SubscriptionService subscriptionService)
        {
            _context = context;
            _configuration = configuration;
            _subscriptionService = subscriptionService;
        }

        public async Task<Charge> ProcessFindersFee(string userId, decimal winningBidAmount, string jobId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                throw new Exception("User not found");
            }

            decimal feePercentage = 0;
            if (user.UserType == "Subcontractor")
            {
                feePercentage = 0.05m; // 5%
            }
            else if (user.UserType == "Vendor")
            {
                feePercentage = 0.025m; // 2.5%
            }
            else
            {
                return null; // No fee for other user types
            }

            var feeAmount = winningBidAmount * feePercentage;
            var feeAmountInCents = (long)(feeAmount * 100);

            StripeConfiguration.ApiKey = Environment.GetEnvironmentVariable("StripeAPIKey") ?? _configuration["StripeAPI:StripeKey"];

            var options = new ChargeCreateOptions
            {
                Amount = feeAmountInCents,
                Currency = "usd",
                Description = $"Finders fee for winning bid",
                Customer = user.StripeCustomerId, // TODO: Assuming we have the Stripe Customer ID here
                // Source = "tok_visa" // TODO: typically get a token from the frontend, need to check
            };

            var service = new ChargeService();
            Charge charge = await service.CreateAsync(options);

            if (charge.Status == "succeeded")
            {
                await _subscriptionService.GrantTemporaryTier1Access(userId, jobId);
            }

            return charge;
        }
    }
}