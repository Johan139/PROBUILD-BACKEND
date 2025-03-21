using Microsoft.AspNetCore.SignalR;

namespace ProbuildBackend.Middleware
{
    public class ProgressHub : Hub
    {
        public async Task SendProgress(string connectionId, int progress)
        {
            await Clients.Client(connectionId).SendAsync("ReceiveProgress", progress);
        }
        public async Task BroadcastProgress(int progress)
        {
            await Clients.All.SendAsync("ReceiveProgress", progress);
        }
    }
}
