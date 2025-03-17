using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Dtos;


namespace ProbuildBackend.Controllers{
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
        public async Task<IActionResult> SendNotification([FromBody] NotificationDto notificationDto)
        {
            var notification = new NotificationModel
            {
                Message = notificationDto.Message,
                ProjectId = notificationDto.ProjectId,
                UserId = notificationDto.UserId,
                Recipients = notificationDto.Recipients,
                Timestamp = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            // Broadcast message to recipients via WebSocket
            await _webSocketManager.BroadcastMessageAsync(notification.Message, notificationDto.Recipients);

            return Ok(new { message = "Notification sent and broadcasted" });
        }


        // GET: api/Notifications/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<NotificationModel>> GetNotification(int id)
        {
            var notification = await _context.Notifications.FindAsync(id);

            if (notification == null)
            {
                return NotFound();
            }

            return notification;
        }

        // GET: api/Notifications
        [HttpGet]
        public async Task<ActionResult<IEnumerable<NotificationModel>>> GetNotifications()
        {
            return await _context.Notifications
                .Include(n => n.User)
                .Include(n => n.Project)
                .ToListAsync();
        }
    }
}