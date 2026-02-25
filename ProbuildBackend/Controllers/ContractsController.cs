using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Services;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContractsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ContractService _contractService;

        public ContractsController(ApplicationDbContext context, ContractService contractService)
        {
            _context = context;
            _contractService = contractService;
        }

        [HttpPost("{jobId}/generate")]
        public async Task<IActionResult> GenerateContract(int jobId)
        {
            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                return NotFound("Job not found.");
            }

            var winningQuote = await _context.Quotes.FirstOrDefaultAsync(q =>
                q.JobID == jobId && q.Status == "AWARDED"
            );

            if (winningQuote == null)
            {
                return BadRequest("No awarded quote found for this job.");
            }

            var contract = new Contract
            {
                JobId = jobId,
                GcId = job.UserId,
                ScVendorId = winningQuote.CreatedID,
                ContractText =
                    "This is a sample contract text. Replace with actual contract generation logic.",
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow,
            };

            _context.Contracts.Add(contract);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GenerateContract), new { id = contract.Id }, contract);
        }

        [HttpPost("{jobId}/generate-general-client-contract")]
        public async Task<IActionResult> GenerateGeneralClientContract(int jobId)
        {
            var job = await _context.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId);
            if (job == null)
            {
                return NotFound("Job not found.");
            }

            var userId = User.FindFirstValue("UserId");
            var gcId = string.IsNullOrWhiteSpace(userId) ? job.UserId : userId;

            if (string.IsNullOrWhiteSpace(gcId))
            {
                return BadRequest("Unable to resolve general contractor account.");
            }

            try
            {
                var contract = await _contractService.GenerateGeneralContractAsync(jobId, gcId);
                return Ok(contract);
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("{contractId}/upload-client-pdf")]
        public async Task<IActionResult> UploadClientContractPdf(Guid contractId, [FromForm] IFormFile file)
        {
            if (file == null)
            {
                return BadRequest("No file was provided.");
            }

            var userId = User.FindFirstValue("UserId");

            try
            {
                var updated = await _contractService.UploadClientContractAsync(contractId, file, userId);
                return Ok(updated);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{contractId}/download-client-pdf")]
        public async Task<IActionResult> DownloadClientContractPdf(Guid contractId)
        {
            try
            {
                var (content, contentType, fileName) =
                    await _contractService.DownloadContractFileAsync(contractId);
                return File(content, contentType, fileName);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
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

        [HttpGet("job/{jobId}")]
        public async Task<IActionResult> GetContractsByJobId(int jobId)
        {
            var contracts = await _context.Contracts.Where(c => c.JobId == jobId).ToListAsync();
            return Ok(contracts);
        }

        [HttpPost("{contractId}/sign")]
        public async Task<IActionResult> SignContract(
            Guid contractId,
            [FromBody] Models.DTO.SignatureDto signature
        )
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

            if (
                !string.IsNullOrEmpty(contract.GcSignature)
                && !string.IsNullOrEmpty(contract.ScVendorSignature)
            )
            {
                contract.Status = "SIGNED";
            }

            await _context.SaveChangesAsync();

            return Ok(contract);
        }
    }
}
