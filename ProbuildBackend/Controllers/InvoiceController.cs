using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Models;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/invoices")]
    public class InvoiceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public InvoiceController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadInvoice(
            IFormFile file,
            [FromForm] int jobId,
            [FromForm] decimal amount
        )
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var uploadsFolderPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "invoices"
            );
            if (!Directory.Exists(uploadsFolderPath))
            {
                Directory.CreateDirectory(uploadsFolderPath);
            }

            var uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
            var filePath = Path.Combine(uploadsFolderPath, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                UploaderId = "user-id-placeholder", // TODO: Replace with actual user ID from claims
                FilePath = $"/invoices/{uniqueFileName}",
                Status = "PENDING",
                Amount = amount,
                UploadedAt = DateTime.UtcNow,
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(UploadInvoice), new { id = invoice.Id }, invoice);
        }
    }
}
