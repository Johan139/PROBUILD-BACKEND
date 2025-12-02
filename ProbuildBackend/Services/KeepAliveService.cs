using ProbuildBackend.Interface;

namespace ProbuildBackend.Services
{
    public class KeepAliveService : IKeepAliveService, IDisposable
    {
        private Timer _timer;
        private readonly HttpClient _httpClient;
        private const int PingIntervalMinutes = 7;
        private readonly string _pingUrl;

        public KeepAliveService(IConfiguration configuration)
        {
            _httpClient = new HttpClient();
            // TODO: Is this right?
            var appUrl = configuration["Jwt:Issuer"] ?? "https://localhost";
            _pingUrl = $"{appUrl}/KeepAlive";
        }

        public void StartPinging()
        {
            _timer = new Timer(
                callback: _ => Ping(),
                state: null,
                dueTime: TimeSpan.FromMinutes(PingIntervalMinutes),
                period: TimeSpan.FromMinutes(PingIntervalMinutes)
            );
        }

        public void StopPinging()
        {
            _timer?.Change(Timeout.Infinite, 0);
        }

        private async void Ping()
        {
            try
            {
                // The request is sent but we don't need to wait for the response
                // The goal is just to generate traffic
                await _httpClient.GetAsync(_pingUrl);
            }
            catch
            {
                // If the ping fails, the main job is likely still running (hopefully)
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _httpClient?.Dispose();
        }
    }
}
