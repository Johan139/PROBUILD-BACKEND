using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using System.Security.Claims;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly WebSocketManager _webSocketManager;

        public NotificationsController(ApplicationDbContext context, WebSocketManager webSocketManager)
        {
            _context = context;
            _webSocketManager = webSocketManager;
        }

        [HttpPost]
        public async Task<IActionResult> SendNotification([FromBody] NotificationModel notification)
        {
            notification.SenderId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            notification.Timestamp = DateTime.UtcNow;

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            // Broadcast message to recipients via WebSocket
            await _webSocketManager.BroadcastMessageAsync(notification.Message, notification.Recipients);

            return Ok(new { message = "Notification sent and broadcasted" });
        }

        [HttpGet("recent")]
        public async Task<ActionResult<IEnumerable<NotificationDto>>> GetRecentNotifications()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var notifications = await _context.NotificationViews
                .Where(n => n.RecipientId == userId)
                .OrderByDescending(n => n.Timestamp)
                .Take(5)
                .Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Message = n.Message,
                    Timestamp = n.Timestamp,
                    ProjectId = n.ProjectId,
                    ProjectName = n.ProjectName,
                    SenderFullName = $"{n.SenderFirstName} {n.SenderLastName}"
                })
                .ToListAsync();

            return Ok(notifications);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<NotificationDto>>> GetNotifications()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var notifications = await _context.NotificationViews
                .Where(n => n.RecipientId == userId)
                .OrderByDescending(n => n.Timestamp)
                .Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Message = n.Message,
                    Timestamp = n.Timestamp,
                    ProjectId = n.ProjectId,
                    ProjectName = n.ProjectName,
                    SenderFullName = $"{n.SenderFirstName} {n.SenderLastName}"
                })
                .ToListAsync();

            return Ok(notifications);
        }
    }
}
