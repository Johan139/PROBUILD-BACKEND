// ProbuildBackend/Controllers/ProjectAnalysisController.cs
using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Models;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
public class ProjectAnalysisController : ControllerBase
{
    private readonly IProjectAnalysisOrchestrator _orchestrator;

    public ProjectAnalysisController(IProjectAnalysisOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartAnalysis([FromForm] StartAnalysisRequest request)
    {
        // TODO: Replace with your actual user authentication logic
        var userId = "default-user"; 
        
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

        // Consider using a background job service here for long-running tasks
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
}

// Data Transfer Objects (DTOs) for API requests
public class StartAnalysisRequest 
{
    public IFormFileCollection? BlueprintImages { get; set; }
    public string JobDetailsJson { get; set; } // e.g., '{"DesiredStartDate":"2024-01-01", ...}'
}
public class RebuttalRequest { public string ClientQuery { get; set; } }
public class RevisionRequest { public string RevisionRequestText { get; set; } }