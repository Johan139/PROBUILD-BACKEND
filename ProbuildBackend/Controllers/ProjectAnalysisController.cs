using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using System.Security.Claims;
using System.Text.Json;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProjectAnalysisController : ControllerBase
{
    private readonly IProjectAnalysisOrchestrator _orchestrator;
    private readonly IAnalysisService _analysisService;
    private readonly ILogger<ProjectAnalysisController> _logger;

    public ProjectAnalysisController(IProjectAnalysisOrchestrator orchestrator, IAnalysisService analysisService, ILogger<ProjectAnalysisController> logger)
    {
        _orchestrator = orchestrator;
        _analysisService = analysisService;
        _logger = logger;
    }

    [HttpPost("start-full-analysis")]
    public async Task<IActionResult> StartAnalysis([FromForm] StartAnalysisRequest request)
    {
        var userId = User.FindFirstValue("UserId");
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var imageByteList = new List<byte[]>();
        if (request.BlueprintImages != null)
        {
            foreach (var file in request.BlueprintImages)
            {
                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                imageByteList.Add(memoryStream.ToArray());
            }
        }

        // The JobDetails might be passed as a JSON string from a form
        var jobDetails = JsonSerializer.Deserialize<JobModel>(request.JobDetailsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (jobDetails == null)
        {
            return BadRequest("Invalid JobDetails JSON.");
        }

        // Consider background job service here for long-running tasks
        var conversationId = await _orchestrator.StartFullAnalysisAsync(userId, imageByteList, jobDetails);

        return Ok(new { Message = "Analysis started successfully. The results are being compiled.", ConversationId = conversationId });
    }

    [HttpPost("{conversationId}/rebuttal")]
    public async Task<IActionResult> PostRebuttal(string conversationId, [FromBody] RebuttalRequest request)
    {
        var response = await _orchestrator.GenerateRebuttalAsync(conversationId, request.ClientQuery);
        return Ok(new { Response = response });
    }

    [HttpPost("{conversationId}/revision")]
    public async Task<IActionResult> PostRevision(string conversationId, [FromBody] RevisionRequest request)
    {
        var response = await _orchestrator.GenerateRevisionAsync(conversationId, request.RevisionRequestText);
        return Ok(new { Response = response });
    }

    [HttpPost("perform-selected")]
    public async Task<IActionResult> PerformSelectedAnalysis([FromBody] AnalysisRequestDto requestDto)
    {
        var userId = User.FindFirstValue("UserId");
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        _logger.LogInformation("User {UserId} initiated a one-shot 'Selected' analysis with prompts: {PromptKeys}",
            userId, string.Join(", ", requestDto.PromptKeys ?? new List<string>()));

        try
        {
            var result = await _analysisService.PerformAnalysisAsync(requestDto);
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
}

// Data Transfer Objects (DTOs) for API requests
public class StartAnalysisRequest
{
    public IFormFileCollection? BlueprintImages { get; set; }
    public string JobDetailsJson { get; set; } // e.g., '{"DesiredStartDate":"2024-01-01", ...}'
}
public class RebuttalRequest { public string ClientQuery { get; set; } }
public class RevisionRequest { public string RevisionRequestText { get; set; } }
