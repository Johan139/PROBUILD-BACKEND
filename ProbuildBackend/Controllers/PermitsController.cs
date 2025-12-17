using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Helpers;
using ProbuildBackend.Models;
using ProbuildBackend.Services;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PermitsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly AzureBlobService _azureBlobservice;

        public PermitsController(ApplicationDbContext context, AzureBlobService azureBlobservice)
        {
            _context = context;
            _azureBlobservice = azureBlobservice;
        }

        [HttpGet("job/{jobId}")]
        public async Task<ActionResult<IEnumerable<JobPermitModel>>> GetPermitsByJobId(int jobId)
        {
            var permits = await _context
                .JobPermits.Where(p => p.JobId == jobId)
                .Include(p => p.Document)
                .ToListAsync();

            return Ok(permits);
        }

        [HttpPost]
        public async Task<ActionResult<JobPermitModel>> PostPermit(JobPermitModel permit)
        {
            if (permit == null)
            {
                return BadRequest("Permit data is required.");
            }

            _context.JobPermits.Add(permit);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPermitsByJobId), new { jobId = permit.JobId }, permit);
        }

        [HttpPost("batch")]
        public async Task<ActionResult<IEnumerable<JobPermitModel>>> PostPermitsBatch(
            List<JobPermitModel> permits
        )
        {
            if (permits == null || !permits.Any())
            {
                return BadRequest("No permits provided.");
            }

            _context.JobPermits.AddRange(permits);
            await _context.SaveChangesAsync();

            return Ok(permits);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutPermit(int id, JobPermitModel permit)
        {
            if (id != permit.Id)
            {
                return BadRequest();
            }

            _context.Entry(permit).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!PermitExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePermit(int id)
        {
            var permit = await _context.JobPermits.FindAsync(id);
            if (permit == null)
            {
                return NotFound();
            }

            _context.JobPermits.Remove(permit);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("{id}/upload")]
        public async Task<IActionResult> UploadPermitDocument(
            int id,
            [FromForm] IFormFile file,
            [FromForm] string sessionId
        )
        {
            var permit = await _context.JobPermits.FindAsync(id);
            if (permit == null)
            {
                return NotFound("Permit not found.");
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest("Invalid file.");
            }

            try
            {
                var folder = $"jobs/{permit.JobId}/permits/{id}";
                var uploadedUrl = await _azureBlobservice.UploadImageAsync(file, folder);

                if (string.IsNullOrEmpty(uploadedUrl))
                {
                    return StatusCode(500, "Failed to upload file to Azure Blob Storage.");
                }

                var jobDocument = new JobDocumentModel
                {
                    JobId = permit.JobId,
                    FileName = file.FileName,
                    BlobUrl = uploadedUrl,
                    SessionId = sessionId ?? Guid.NewGuid().ToString(),
                    UploadedAt = DateTime.UtcNow,
                    Size = file.Length,
                    Type = "Permit",
                };

                _context.JobDocuments.Add(jobDocument);
                await _context.SaveChangesAsync();

                // Link document to permit
                permit.DocumentId = jobDocument.Id;
                await _context.SaveChangesAsync();

                return Ok(new { url = uploadedUrl, documentId = jobDocument.Id });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        private bool PermitExists(int id)
        {
            return _context.JobPermits.Any(e => e.Id == id);
        }
    }
}
