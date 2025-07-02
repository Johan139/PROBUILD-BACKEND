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
            Console.WriteLine("UploadLogo called");

            try
            {
                if (file == null || file.Length == 0)
                {
                    Console.WriteLine("File was null or empty.");
                    return BadRequest("Invalid file.");
                }

                Console.WriteLine($"File received: {file.FileName}, {file.Length} bytes, content-type: {file.ContentType}");

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);

                Console.WriteLine("File copied to memory stream");

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

                Console.WriteLine("Logo model created");

                _context.Logos.Add(logo);
                Console.WriteLine("Added to context");

                await _context.SaveChangesAsync();
                Console.WriteLine("Saved to database");

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
