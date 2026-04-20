using Microsoft.EntityFrameworkCore;

namespace BuildigBackend.Services
{
    public class TokenCleanupService(IServiceProvider services, ILogger<TokenCleanupService> logger)
        : BackgroundService
    {
        private readonly IServiceProvider _services = services;
        private readonly ILogger<TokenCleanupService> _logger = logger;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Token Cleanup Service is starting.");

            // Avoid doing heavy cleanup immediately on boot (can spam logs / DB after downtime).
            // A short delay also lets the app come up fully before running maintenance tasks.
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

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

            try
            {
                // Execute as a single SQL statement (fast, avoids N DELETE statements).
                var deletedCount = await context
                    .RefreshTokens
                    .Where(rt => rt.Expires < DateTime.UtcNow || rt.Revoked != null)
                    .ExecuteDeleteAsync(stoppingToken);

                if (deletedCount != 0)
                {
                    _logger.LogInformation($"Cleaned up {deletedCount} refresh token(s).");
                }
                else
                {
                    _logger.LogInformation("No refresh tokens to clean up.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean up refresh tokens.");
            }
        }
    }
}

