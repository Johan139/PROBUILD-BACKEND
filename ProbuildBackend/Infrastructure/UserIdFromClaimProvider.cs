using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace ProbuildBackend.Infrastructure
{
    public class UserIdFromClaimProvider : IUserIdProvider
    {
        private const string ClaimName = "UserId"; // matches your tokens

        public string? GetUserId(HubConnectionContext connection)
        {
            // Prefer your custom claim; fall back to NameIdentifier if present
            return connection.User?.FindFirst(ClaimName)?.Value
                ?? connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }
}
