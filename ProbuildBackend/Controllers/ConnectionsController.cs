using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;

namespace Probuild.Controllers
{
    [Authorize]
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
            var requesterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var connections = await _context.Connections
                .Where(c => (c.RequesterId == userId || c.ReceiverId == userId))
                .ToListAsync();

            return Ok(connections);
        }
    }

    public class ConnectionRequest
    {
        public string ReceiverId { get; set; }
    }
}
