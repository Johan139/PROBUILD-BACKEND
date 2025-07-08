using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Models;
using Microsoft.EntityFrameworkCore;

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
