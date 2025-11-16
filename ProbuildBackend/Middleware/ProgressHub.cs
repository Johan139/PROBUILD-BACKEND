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

    public class ProgressHub : Hub
    {
        public async Task SendProgress(string connectionId, int progress)
        {
            await Clients.Client(connectionId).SendAsync("ReceiveProgress", progress);
        }

        public async Task SendAnalysisProgress(string connectionId, AnalysisProgressUpdate update)
        {
            await Clients.Client(connectionId).SendAsync("ReceiveAnalysisProgress", update);
        }

        public async Task BroadcastProgress(int progress)
        {
            await Clients.All.SendAsync("ReceiveProgress", progress);
        }
    }
}
