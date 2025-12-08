using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ProbuildBackend.Middleware
{
    public class AnalysisProgressUpdate
    {
        public int JobId { get; set; }
        public string StatusMessage { get; set; }
        public int CurrentStep { get; set; }
        public int TotalSteps { get; set; }
        public bool IsComplete { get; set; }
        public bool HasFailed { get; set; }
        public string ErrorMessage { get; set; }
    }

    [Authorize]
    public class ProgressHub : Hub
    {
        private readonly ILogger<ProgressHub> _logger;

        public ProgressHub(ILogger<ProgressHub> logger)
        {
            _logger = logger;
            _logger.LogInformation("ProgressHub instance created.");
        }

        public override Task OnConnectedAsync()
        {
            _logger.LogInformation(
                "Client connected to ProgressHub: {ConnectionId}",
                Context.ConnectionId
            );
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            if (exception != null)
                _logger.LogWarning(
                    exception,
                    "Client disconnected from ProgressHub due to error: {ConnectionId}",
                    Context.ConnectionId
                );
            else
                _logger.LogInformation(
                    "Client disconnected from ProgressHub: {ConnectionId}",
                    Context.ConnectionId
                );
            return base.OnDisconnectedAsync(exception);
        }

        public async Task SendProgress(string connectionId, int progress)
        {
            _logger.LogInformation(
                "Sending progress {Progress} to {ConnectionId}",
                progress,
                connectionId
            );
            await Clients.Client(connectionId).SendAsync("ReceiveProgress", progress);
        }

        public async Task SendAnalysisProgress(string connectionId, AnalysisProgressUpdate update)
        {
            _logger.LogInformation(
                "Sending analysis progress for JobId {JobId} to {ConnectionId}",
                update.JobId,
                connectionId
            );
            await Clients.Client(connectionId).SendAsync("ReceiveAnalysisProgress", update);
        }

        public async Task BroadcastProgress(int progress)
        {
            _logger.LogInformation("Broadcasting progress {Progress} to all clients", progress);
            await Clients.All.SendAsync("ReceiveProgress", progress);
        }
    }
}
