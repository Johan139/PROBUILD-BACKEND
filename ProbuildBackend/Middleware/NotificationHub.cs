using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace ProbuildBackend.Middleware
{
    public class NotificationHub : Hub
    {
        public override Task OnConnectedAsync()
        {
            return base.OnConnectedAsync();
        }
    }
}