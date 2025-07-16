using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Middleware;
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
        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationsController(ApplicationDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpPost]
        public async Task<IActionResult> SendNotification([FromBody] NotificationModel notification)
        {
            notification.SenderId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            notification.Timestamp = DateTime.UtcNow;

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            // Broadcast message to recipients via WebSocket
            await _hubContext.Clients.All.SendAsync("ReceiveNotification", notification);

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

        [HttpPost("test")]
        public async Task<IActionResult> SendTestNotification()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var testNotification = new NotificationModel
            {
                Message = "This is a test notification.",
                Timestamp = DateTime.UtcNow,
                ProjectId = 356, // DDTHernandez - multi test 3
                UserId = userId, // Recipient: Daniel Davies
                SenderId = "483284e7-a356-43c6-b399-c3af452e879b", // Sender: sdafewf asdfaewf
                Recipients = new List<string> { userId }
            };

            _context.Notifications.Add(testNotification);
            await _context.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("ReceiveNotification", testNotification);
            return Ok(new { message = "Test notification created successfully." });
        }
    }
}
