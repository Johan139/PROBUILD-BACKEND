using Microsoft.AspNetCore.Authorization;
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

            // Send notification to each recipient
            foreach (var recipientId in notification.Recipients)
            {
                await _hubContext.Clients.User(recipientId).SendAsync("ReceiveNotification", notification);
            }

            return Ok(new { message = "Notification sent successfully" });
        }

        [HttpGet("recent")]
        public async Task<ActionResult<IEnumerable<NotificationDto>>> GetRecentNotifications()
        {
            var userId = User.FindFirstValue("UserId");
            var notifications = await _context.NotificationViews
                .Where(n => n.RecipientId == userId)
                .OrderByDescending(n => n.Timestamp)
                .Take(5)
                .Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Message = n.Message,
                    Timestamp = n.Timestamp,
                    JobId = n.JobId,
                    ProjectName = n.ProjectName,
                    SenderFullName = n.SenderId == "system" ? "System" : $"{n.SenderFirstName} {n.SenderLastName}"
                })
                .ToListAsync();

            return Ok(notifications);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<NotificationDto>>> GetNotifications()
        {
            var userId = User.FindFirstValue("UserId");
            var notifications = await _context.NotificationViews
                .Where(n => n.RecipientId == userId)
                .OrderByDescending(n => n.Timestamp)
                .Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Message = n.Message,
                    Timestamp = n.Timestamp,
                    JobId = n.JobId,
                    ProjectName = n.ProjectName,
                    SenderFullName = n.SenderId == "system" ? "System" : $"{n.SenderFirstName} {n.SenderLastName}"
                })
                .ToListAsync();

            return Ok(notifications);
        }

        [HttpPost("test")]
        [Authorize]
        public async Task<IActionResult> SendTestNotification()
        {
            if (Request.Headers.ContainsKey("Authorization"))
            {
                var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            }
            else
            {
                Console.WriteLine("No Authorization header found!");
            }

            var userId = User.FindFirstValue("UserId");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { error = "User ID not found in token" });
            }

            // Check if Job exists
            var jobExists = await _context.Jobs.AnyAsync(j => j.Id == 356);

            // Use first available job if 356 doesn't exist
            var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == 356) ??
                    await _context.Jobs.FirstOrDefaultAsync();

            if (job == null)
            {
                return BadRequest(new { error = "No jobs available" });
            }

            var testNotification = new NotificationModel
            {
                Message = "This is a test notification.",
                Timestamp = DateTime.UtcNow,
                JobId = job.Id,
                UserId = userId,
                SenderId = userId, // Use current user as sender
                Recipients = new List<string> { userId }
            };

            try
            {
                _context.Notifications.Add(testNotification);
                await _context.SaveChangesAsync();

                // Send notification to each recipient
                foreach (var recipientId in testNotification.Recipients)
                {
                    await _hubContext.Clients.User(recipientId).SendAsync("ReceiveNotification", testNotification);
                }

                return Ok(new {
                    message = "Test notification created successfully.",
                    jobId = job.Id,
                    userId = userId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to save notification", details = ex.Message });
            }
        }
    }
}
