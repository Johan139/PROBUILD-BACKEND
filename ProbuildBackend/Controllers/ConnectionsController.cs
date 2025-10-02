using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace Probuild.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConnectionsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ConnectionsController(ApplicationDbContext context)
        {
            _context = context;
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

            _context.Connections.Add(connection);
            await _context.SaveChangesAsync();

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
