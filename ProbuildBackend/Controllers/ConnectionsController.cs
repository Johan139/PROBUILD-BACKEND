using Hangfire.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using Stripe;
using System.Security.Claims;

namespace Probuild.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConnectionsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailTemplateService _emailTemplate;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;

        public ConnectionsController(ApplicationDbContext context, IEmailTemplateService emailTemplate, IEmailSender emailSender, IConfiguration configuration)
        {
            _context = context;
            _emailTemplate = emailTemplate;
            _emailSender = emailSender;
            _configuration = configuration;
        }

        [HttpPost("request")]
        public async Task<IActionResult> RequestConnection([FromBody] ConnectionRequest request)
        {
            var requesterId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(requesterId))
            {
                return Unauthorized();
            }

            var existingConnection = await _context.Connections
                .FirstOrDefaultAsync(c =>
                    (c.RequesterId == requesterId && c.ReceiverId == request.ReceiverId) ||
                    (c.RequesterId == request.ReceiverId && c.ReceiverId == requesterId));

            if (existingConnection != null)
            {
                return BadRequest("A connection or pending request already exists between these users.");
            }

            var connection = new Connection
            {
                Id = Guid.NewGuid(),
                RequesterId = requesterId,
                ReceiverId = request.ReceiverId,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var receiver =  _context.Users.Where(u => u.Id == request.ReceiverId).FirstOrDefault();
            var requester =  _context.Users.Where(u => u.Id == requesterId).FirstOrDefault();

            var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? _configuration["FrontEnd:FRONTEND_URL"];
            var callbackURL = $"{frontendUrl}/connections";

            _context.Connections.Add(connection);
            await _context.SaveChangesAsync();

            var ConnectionEmail = await _emailTemplate.GetTemplateAsync("ConnectionRequestEmail");

            ConnectionEmail.Subject = ConnectionEmail.Subject.Replace("{{InviterName}}", requester.FirstName + " " + requester.LastName);

            ConnectionEmail.Body = ConnectionEmail.Body.Replace("{{UserName}}", receiver.FirstName + " " + receiver.LastName).Replace("{{ConnectionLink}}", callbackURL)
                .Replace("{{InviterName}}", requester.FirstName + " " + requester.LastName)
                .Replace("{{Header}}", ConnectionEmail.HeaderHtml)
                .Replace("{{Footer}}", ConnectionEmail.FooterHtml);

            await _emailSender.SendEmailAsync(ConnectionEmail, receiver.Email);

            return CreatedAtAction(nameof(GetConnections), new { id = connection.Id }, connection);
        }

        [HttpPost("{connectionId}/accept")]
        public async Task<IActionResult> AcceptConnection(Guid connectionId)
        {
            var userId = User.FindFirstValue("UserId");
            var connection = await _context.Connections.FindAsync(connectionId);

            if (connection == null || connection.ReceiverId != userId || connection.Status != "PENDING")
            {
                return NotFound();
            }

            connection.Status = "ACCEPTED";
            connection.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("{connectionId}/decline")]
        public async Task<IActionResult> DeclineConnection(Guid connectionId)
        {
            var userId = User.FindFirstValue("UserId");
            var connection = await _context.Connections.FindAsync(connectionId);

            if (connection == null || connection.ReceiverId != userId || connection.Status != "PENDING")
            {
                return NotFound();
            }

            connection.Status = "DECLINED";
            connection.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> GetConnections()
        {
            var userId = User.FindFirstValue("UserId");

            var connections = await _context.Connections
                .Where(c => c.RequesterId == userId || c.ReceiverId == userId)
                .Select(c => new ConnectionDto
                {
                    Id = c.Id.ToString(),
                    OtherUserId = c.RequesterId == userId ? c.ReceiverId : c.RequesterId,
                    Status = c.Status,
                    IsInSystem = true,
                    RequesterId = c.RequesterId,
                    ReceiverId = c.ReceiverId
                })
                .ToListAsync();

            var invitations = await _context.Invitations
                .Where(i => i.InviterId == userId)
                .Select(i => new ConnectionDto
                {
                    Id = i.Id.ToString(),
                    OtherUserEmail = i.InviteeEmail,
                    Status = i.IsAccepted ? "ACCEPTED" : "PENDING",
                    IsInSystem = false,
                    FirstName = i.FirstName,
                    LastName = i.LastName,
                    RequesterId = i.InviterId
                })
                .ToListAsync();

            var allConnections = connections.Concat(invitations);
            return Ok(allConnections);
        }

        [HttpGet("incoming")]
        public async Task<IActionResult> GetIncomingRequests()
        {
            var userId = User.FindFirstValue("UserId");
            var requests = await _context.Connections
                .Where(c => c.ReceiverId == userId && c.Status == "PENDING")
                .Include(c => c.Requester)
                .ToListAsync();
            return Ok(requests);
        }

        [HttpGet("outgoing")]
        public async Task<IActionResult> GetOutgoingRequests()
        {
            var userId = User.FindFirstValue("UserId");
            var requests = await _context.Connections
                .Where(c => c.RequesterId == userId && c.Status == "PENDING")
                .Include(c => c.Receiver)
                .ToListAsync();
            return Ok(requests);
        }

        [HttpPost("{connectionId}/remove")]
        public async Task<IActionResult> RemoveConnection(Guid connectionId)
        {
            var userId = User.FindFirstValue("UserId");
            var connection = await _context.Connections.FindAsync(connectionId);

            if (connection == null || (connection.RequesterId != userId && connection.ReceiverId != userId))
            {
                return NotFound();
            }

            _context.Connections.Remove(connection);
            await _context.SaveChangesAsync();

            return Ok();
        }   
    }

    public class ConnectionRequest
    {
        public string ReceiverId { get; set; }
    }
}
