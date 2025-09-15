using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SubscriptionController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public SubscriptionController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("unsubscribe")]
        public async Task<IActionResult> Unsubscribe(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest("Email address is required.");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user != null)
            {
                user.NotificationEnabled = false;
                await _context.SaveChangesAsync();
                return Ok("You have been unsubscribed from new job notifications.");
            }

            var externalUser = await _context.JobNotificationRecipients.FirstOrDefaultAsync(r => r.email == email);
            if (externalUser != null)
            {
                externalUser.notification_enabled = false;
                await _context.SaveChangesAsync();
                return Ok("You have been unsubscribed from new job notifications.");
            }

            return NotFound("Email address not found.");
        }
    }
}