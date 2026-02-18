// Controllers/JobStatusController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Services;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class JobStatusController : ControllerBase
    {
        private readonly IProgressService _progressService;
        private readonly ApplicationDbContext _context;

        public JobStatusController(
            IProgressService progressService,
            ApplicationDbContext context)
        {
            _progressService = progressService;
            _context = context;
        }

        [HttpGet("{jobId:int}/status")]
        public async Task<IActionResult> GetJobStatus(int jobId)
        {
            // First check in-memory cache
            var progress = _progressService.GetJobProgress(jobId);

            if (progress != null)
            {
                return Ok(new
                {
                    jobId = progress.JobId,
                    status = progress.Status,
                    percent = progress.Percent,
                    currentStep = progress.CurrentStep,
                    totalSteps = progress.TotalSteps,
                    message = progress.Message,
                    resultUrl = progress.ResultUrl,
                    error = progress.ErrorMessage
                });
            }

            // Fallback: check DB for final status (completed/failed jobs)
            var job = await _context.Jobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job == null)
            {
                return NotFound(new { error = "Job not found" });
            }

            var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL")
                ?? "http://localhost:4200";

            return Ok(new
            {
                jobId = job.Id,
                status = job.Status,
                percent = job.Status == "PROCESSED" ? 100 : 0,
                currentStep = job.Status == "PROCESSED" ? 32 : 0,
                totalSteps = 32,
                message = job.Status == "PROCESSED" ? "Analysis complete" : null,
                resultUrl = job.Status == "PROCESSED" ? $"{frontendUrl}/view-quote?jobId={job.Id}" : null,
                error = job.Status == "FAILED" ? "Analysis failed" : null
            });
        }

        [HttpPost("{jobId:int}/reconnect")]
        public IActionResult Reconnect(int jobId, [FromBody] ReconnectRequest request)
        {
            if (string.IsNullOrEmpty(request.ConnectionId))
            {
                return BadRequest(new { error = "ConnectionId is required" });
            }

            _progressService.SetConnectionId(jobId, request.ConnectionId);

            var progress = _progressService.GetJobProgress(jobId);

            if (progress == null)
            {
                return NotFound(new { error = "Job progress not found - may have completed" });
            }

            return Ok(new
            {
                jobId = progress.JobId,
                status = progress.Status,
                percent = progress.Percent,
                currentStep = progress.CurrentStep,
                totalSteps = progress.TotalSteps,
                message = progress.Message
            });
        }
    }

    public class ReconnectRequest
    {
        public string ConnectionId { get; set; } = string.Empty;
    }
}