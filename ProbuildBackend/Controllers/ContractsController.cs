using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContractsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ContractsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("{jobId}/generate")]
        public async Task<IActionResult> GenerateContract(int jobId)
        {
            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                return NotFound("Job not found.");
            }

            var winningQuote = await _context.Quotes
                .FirstOrDefaultAsync(q => q.JobID == jobId && q.Status == "AWARDED");

            if (winningQuote == null)
            {
                return BadRequest("No awarded quote found for this job.");
            }

            var contract = new Contract
            {
                JobId = jobId,
                GcId = job.UserId,
                ScVendorId = winningQuote.CreatedID,
                ContractText = "This is a sample contract text. Replace with actual contract generation logic.",
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow
            };

            _context.Contracts.Add(contract);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GenerateContract), new { id = contract.Id }, contract);
        }

        [HttpGet("{contractId}")]
        public async Task<IActionResult> GetContract(Guid contractId)
        {
            var contract = await _context.Contracts.FindAsync(contractId);
            if (contract == null)
            {
                return NotFound("Contract not found.");
            }
            return Ok(contract);
        }

        [HttpPost("{contractId}/sign")]
        public async Task<IActionResult> SignContract(Guid contractId, [FromBody] Models.DTO.SignatureDto signature)
        {
            var contract = await _context.Contracts.FindAsync(contractId);
            if (contract == null)
            {
                return NotFound("Contract not found.");
            }

            var userId = User.FindFirstValue("UserId");

            if (userId == contract.GcId)
            {
                contract.GcSignature = signature.Signature;
            }
            else if (userId == contract.ScVendorId)
            {
                contract.ScVendorSignature = signature.Signature;
            }
            else
            {
                return Forbid();
            }

            if (!string.IsNullOrEmpty(contract.GcSignature) && !string.IsNullOrEmpty(contract.ScVendorSignature))
            {
                contract.Status = "SIGNED";
            }

            await _context.SaveChangesAsync();

            return Ok(contract);
        }
    }
}