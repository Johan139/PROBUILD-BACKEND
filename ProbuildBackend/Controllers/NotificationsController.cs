using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Models.Enums;
using System.Security.Claims;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationsController(
            ApplicationDbContext context,
            IHubContext<NotificationHub> hubContext
        )
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpPost]
        public async Task<IActionResult> SendNotification([FromBody] NotificationModel notification)
        {
            var senderId = User.FindFirstValue("UserId");
            notification.SenderId = senderId;
            notification.Timestamp = DateTime.UtcNow;

            var parsedType = Enum.TryParse<NotificationType>(notification.Type, out var notifType);

            if (!parsedType)
                return BadRequest("Invalid notification type.");

            var allowedRecipients = new List<string>();

            foreach (var recipientId in notification.Recipients)
            {
           

                var isEnabled = await _context.UserNotificationPreferences
                    .AnyAsync(p =>
                        p.UserId == recipientId &&
                        p.NotificationType == notifType &&
                        p.Channel == NotificationChannel.InApp &&
                        p.IsEnabled);

                if (isEnabled)
                    allowedRecipients.Add(recipientId);
            }

            if (!allowedRecipients.Any())
                return Ok(new { message = "Notification blocked by user preferences." });

            notification.Recipients = allowedRecipients;

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            foreach (var recipientId in allowedRecipients)
            {
                await _hubContext
                    .Clients.User(recipientId)
                    .SendAsync("ReceiveNotification", notification);
            }

            return Ok(new { message = "Notification sent respecting preferences." });
        }

        [HttpGet("recent")]
        public async Task<ActionResult<IEnumerable<NotificationDto>>> GetRecentNotifications()
        {
            var userId = User.FindFirstValue("UserId");
            var notifications = await _context
                .NotificationViews.AsNoTracking().Where(n => n.RecipientId == userId)
                .OrderByDescending(n => n.Timestamp)
                .Take(5)
                .Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Message = n.Message,
                    Timestamp = n.Timestamp,
                    JobId = n.JobId,
                    ProjectName = n.ProjectName,
                    SenderFullName =
                        n.SenderId == "system"
                            ? "System"
                            : $"{n.SenderFirstName} {n.SenderLastName}",
                })
                .ToListAsync();

            return Ok(notifications);
        }

        [HttpGet]
        public async Task<ActionResult<PaginatedNotificationResponse>> GetNotifications(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10
        )
        {
            try
            {


            var userId = User.FindFirstValue("UserId");

            // validation for page and pageSize
            if (page <= 0)
                page = 1;
            if (pageSize <= 0)
                pageSize = 10;
            // cap the max page size to prevent abuse
            if (pageSize > 100)
                pageSize = 100;

            var query = _context.NotificationViews.AsNoTracking().Where(n => n.RecipientId == userId);

            var totalCount = await query.CountAsync();

            var notifications = await query
                .OrderByDescending(n => n.Timestamp)
                .Skip((page - 1) * pageSize) // Skips the notifications from previous pages
                .Take(pageSize) // Takes the number of notifications for the current page
                .Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Message = n.Message,
                    Timestamp = n.Timestamp,
                    JobId = n.JobId,
                    ProjectName = n.ProjectName,
                    SenderFullName =
                        n.SenderId == "system"
                            ? "System"
                            : $"{n.SenderFirstName} {n.SenderLastName}",
                    IsRead = n.IsRead,
                    ReadAt = n.ReadAt,
                    QuoteId = n.QuoteId,
                    Type = n.Type,

                })
                .ToListAsync();

            var response = new PaginatedNotificationResponse
            {
                Notifications = notifications,
                TotalCount = totalCount,
            };

            return Ok(response);
            }
            catch (Exception ex)
            {

                throw;
            }
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
            var job =
                await _context.Jobs.FirstOrDefaultAsync(j => j.Id == 356)
                ?? await _context.Jobs.FirstOrDefaultAsync();

            if (job == null)
            {
                return BadRequest(new { error = "No jobs available" });
            }

            var testNotification = new NotificationModel
            {
                Message = "This is a test notification.",
                Timestamp = DateTime.UtcNow,
                JobId = job.Id,
                SenderId = userId, // Use current user as sender
                Recipients = new List<string> { userId },
            };

            try
            {
                _context.Notifications.Add(testNotification);
                await _context.SaveChangesAsync();

                // Send notification to each recipient
                foreach (var recipientId in testNotification.Recipients)
                {
                    await _hubContext
                        .Clients.User(recipientId)
                        .SendAsync("ReceiveNotification", testNotification);
                }

                return Ok(
                    new
                    {
                        message = "Test notification created successfully.",
                        jobId = job.Id,
                        userId = userId,
                    }
                );
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new { error = "Failed to save notification", details = ex.Message }
                );
            }
        }
        [HttpGet("preferences")]
        public async Task<IActionResult> GetPreferences()
        {
            var userId = User.FindFirstValue("UserId");



            var prefs = await _context.UserNotificationPreferences
                .Where(p => p.UserId == userId)
                .Select(p => new
                {
                    p.NotificationType,
                    p.Channel,
                    p.IsEnabled
                })
                .ToListAsync();

            return Ok(prefs);
        }
        [HttpPut("preferences")]
        public async Task<IActionResult> UpdatePreference([FromBody] UpdateNotificationPreferenceDto dto)
        {
            var userId = User.FindFirstValue("UserId");

            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var pref = await _context.UserNotificationPreferences
                .FirstOrDefaultAsync(p =>
                    p.UserId == userId &&
                    p.NotificationType == dto.NotificationType &&
                    p.Channel == dto.Channel);

            if (pref == null)
            {
                // INSERT new preference
                pref = new UserNotificationPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    NotificationType = dto.NotificationType,
                    Channel = dto.Channel,
                    IsEnabled = dto.IsEnabled,
                    CreatedDate = DateTime.UtcNow
                };

                _context.UserNotificationPreferences.Add(pref);
            }
            else
            {
                // UPDATE existing preference
                pref.IsEnabled = dto.IsEnabled;
                pref.ModifiedDate = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Preference saved." });
        }
        [HttpPost("mark-as-read/{id}")]
        public async Task<ActionResult<IActionResult>> MarkReadNotification(int id)
        {
            try
            {
                var userId = User.FindFirstValue("UserId");

                var notification = await _context.NotificationViews
                    .Where(v => v.Id == id && v.RecipientId == userId)
                    .FirstOrDefaultAsync();

                if (notification == null)
                    return NotFound();

                notification.ReadAt = DateTime.UtcNow;
                notification.IsRead = true;

                await _context.SaveChangesAsync();

                return null;
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        [HttpPost("mark-all-as-read")]
        public async Task<IActionResult> MarkAllReadNotification()
        {
            var userId = User.FindFirstValue("UserId");
            var readDate = DateTime.UtcNow;

            var notificationIds = await _context
                .NotificationViews.Where(v => v.RecipientId == userId && v.IsRead != true)
                .Select(v => v.Id)
                .ToListAsync();

            if (!notificationIds.Any())
                return Ok(new { message = "Nothing to mark as read." });

            var notifications = await _context
                .Notifications.Where(n => notificationIds.Contains(n.Id))
                .ToListAsync();

            foreach (var n in notifications)
            {
                n.IsRead = true;
                n.ReadAt = readDate;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Marked as read." });
        }
    }
}
