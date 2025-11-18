using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;
using System.Security.Claims;

namespace ProbuildBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AiController : ControllerBase
    {
        private readonly ILogger<AiController> _logger;
        private readonly IAiAnalysisService _aiAnalysisService;

        public AiController(IAiAnalysisService aiAnalysisService, ILogger<AiController> logger)
        {
            _aiAnalysisService = aiAnalysisService;
            _logger = logger;
        }

        [HttpPost("perform-selected")]
        public async Task<IActionResult> PerformSelectedAnalysis([FromBody] AnalysisRequestDto requestDto)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            if (requestDto == null || requestDto.PromptKeys == null || !requestDto.PromptKeys.Any() || requestDto.DocumentUrls == null || !requestDto.DocumentUrls.Any())
            {
                _logger.LogWarning("User {UserId} made an invalid analysis request with missing prompts or documents.", userId);
                return BadRequest(new { message = "Invalid request. Please provide at least one prompt and one document URL." });
            }

            _logger.LogInformation("User {UserId} initiated a one-shot 'Selected' analysis with prompts: {PromptKeys}",
                userId, string.Join(", ", requestDto.PromptKeys));

            try
            {
                var result = await _aiAnalysisService.PerformSelectedAnalysisAsync(userId, requestDto, requestDto.GenerateDetailsWithAi, requestDto.BudgetLevel);
                return Ok(new { report = result });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid analysis request from user {UserId}", userId);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during analysis for user {UserId}", userId);
                return StatusCode(500, new { message = "An unexpected error occurred during the analysis." });
            }
        }

        [HttpPost("{conversationId}/rebuttal")]
        public async Task<IActionResult> PostRebuttal(string conversationId, [FromBody] RebuttalRequest request)
        {
            var response = await _aiAnalysisService.GenerateRebuttalAsync(conversationId, request.ClientQuery);
            return Ok(new { Response = response });
        }

        [HttpPost("{conversationId}/revision")]
        public async Task<IActionResult> PostRevision(string conversationId, [FromBody] RevisionRequest request)
        {
            var response = await _aiAnalysisService.GenerateRevisionAsync(conversationId, request.RevisionRequestText);
            return Ok(new { Response = response });
        }

        [HttpPost("renovation/analyze")]
        public async Task<IActionResult> AnalyzeRenovation([FromBody] AnalysisRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var jobDetails = new ProbuildBackend.Models.JobModel
            {
                Id = request.JobId,
                UserId = userId
            };

            var response = await _aiAnalysisService.PerformRenovationAnalysisAsync(userId, request.DocumentUrls, jobDetails, request.GenerateDetailsWithAi, request.UserContext, request.UserContextFileUrl, request.BudgetLevel);
            return Ok(new { report = response });
        }

        [HttpPost("comparison/analyze")]
        public async Task<IActionResult> AnalyzeComparison([FromForm] ComparisonAnalysisRequestDto request, [FromForm] List<IFormFile> pdfFiles)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _aiAnalysisService.PerformComparisonAnalysisAsync(request, pdfFiles);
            return Ok(response);
        }
    }

    // Data Transfer Objects (DTOs) for API requests
    public class RebuttalRequest { public string ClientQuery { get; set; } }
    public class RevisionRequest { public string RevisionRequestText { get; set; } }
}
