namespace ProbuildBackend.Services
{
    public class TokenCleanupService(IServiceProvider services, ILogger<TokenCleanupService> logger) : BackgroundService
    {
        private readonly IServiceProvider _services = services;
        private readonly ILogger<TokenCleanupService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Token Cleanup Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Token Cleanup Service is running.");
                await DoWork(stoppingToken);
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken); // Run once a day
            }

            _logger.LogInformation("Token Cleanup Service is stopping.");
        }

        private async Task DoWork(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Cleaning up expired and revoked refresh tokens.");

            using var scope = _services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tokensToRemove = context.RefreshTokens
                .Where(rt => rt.Expires < DateTime.UtcNow || rt.Revoked != null)
                .ToList();

            if (tokensToRemove.Count != 0)
            {
                context.RefreshTokens.RemoveRange(tokensToRemove);
                await context.SaveChangesAsync(stoppingToken);
                _logger.LogInformation($"Cleaned up {tokensToRemove.Count} refresh token(s).");
            }
            else
            {
                _logger.LogInformation("No refresh tokens to clean up.");
            }
        }
    }
}
