using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using System.Security.Claims;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogosController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public LogosController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> UploadLogo([FromForm] IFormFile file, [FromForm] string type, [FromForm] string uploadedBy)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest("Invalid file.");
                }

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);

                var base64 = Convert.ToBase64String(ms.ToArray());

                var logo = new LogosModel
                {
                    Id = Guid.NewGuid(),
                    Url = $"data:{file.ContentType};base64,{base64}",
                    FileName = file.FileName,
                    UploadedBy = uploadedBy,
                    Type = type,
                    UploadedAt = DateTime.Now,
                };
                _context.Logos.Add(logo);

                await _context.SaveChangesAsync();

                return Ok(logo);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR in UploadLogo] {e.Message}\n{e.StackTrace}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("user-logo")]
        public async Task<IActionResult> SetUserLogo([FromForm] IFormFile file)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest("Invalid file.");
            }

            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var base64 = Convert.ToBase64String(ms.ToArray());
                var url = $"data:{file.ContentType};base64,{base64}";

                var existingLogo = await _context.Logos.FirstOrDefaultAsync(l => l.UploadedBy == userId && l.Type == "user_logo");

                if (existingLogo != null)
                {
                    existingLogo.Url = url;
                    existingLogo.FileName = file.FileName;
                    existingLogo.UploadedAt = DateTime.Now;
                    _context.Logos.Update(existingLogo);
                }
                else
                {
                    var newLogo = new LogosModel
                    {
                        Id = Guid.NewGuid(),
                        Url = url,
                        FileName = file.FileName,
                        UploadedBy = userId,
                        Type = "user_logo",
                        UploadedAt = DateTime.Now,
                    };
                    _context.Logos.Add(newLogo);
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ERROR in SetUserLogo] {e.Message}\n{e.StackTrace}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("user-logo")]
        public async Task<IActionResult> GetUserLogo()
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var logo = await _context.Logos.FirstOrDefaultAsync(l => l.UploadedBy == userId && l.Type == "user_logo");
            if (logo == null)
                return NotFound();

            return Ok(logo);
        }

        [HttpDelete("user-logo")]
        public async Task<IActionResult> DeleteUserLogo()
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var logo = await _context.Logos.FirstOrDefaultAsync(l => l.UploadedBy == userId && l.Type == "user_logo");
            if (logo != null)
            {
                _context.Logos.Remove(logo);
                await _context.SaveChangesAsync();
            }

            return NoContent();
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetLogo(Guid id)
        {
            var logo = await _context.Logos.FindAsync(id);
            if (logo == null)
                return NotFound();

            return Ok(logo);
        }
    }
}
