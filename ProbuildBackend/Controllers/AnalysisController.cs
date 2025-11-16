using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using System.Security.Claims;

namespace ProbuildBackend.Controllers
{
  [ApiController]
  [Route("api/[controller]")]
  public class AnalysisController : ControllerBase
  {
    private readonly IWalkthroughService _walkthroughService;
    private readonly ApplicationDbContext _context;
    private readonly IAiAnalysisService _aiAnalysisService;
    private readonly IAiService _aiService;
    private readonly IPromptManagerService _promptManager;


    public AnalysisController(IWalkthroughService walkthroughService, ApplicationDbContext context, IAiAnalysisService aiAnalysisService, IAiService aiService, IPromptManagerService promptManager)
    {
      _walkthroughService = walkthroughService;
      _context = context;
      _aiAnalysisService = aiAnalysisService;
      _aiService = aiService;
      _promptManager = promptManager;
    }

    [HttpPost("fire-and-forget")]
    public async Task<IActionResult> StartFireAndForgetAnalysis()
    {
      throw new NotImplementedException();
    }

    [HttpPost("walkthrough/start")]
    public async Task<IActionResult> StartWalkthrough([FromBody] StartWalkthroughRequestDto request)
    {
      var userId = User.FindFirstValue("UserId");
      if (string.IsNullOrEmpty(userId))
      {
        return Unauthorized();
      }

      // Step 1: Create a placeholder Job to get an ID
      var tempJob = new JobModel
      {
        ProjectName = "Walkthrough In Progress...",
        JobType = "PENDING",
        Qty = 1,
        DesiredStartDate = request.StartDate,
        WallStructure = "PENDING",
        WallInsulation = "PENDING",
        RoofStructure = "PENDING",
        RoofInsulation = "PENDING",
        Foundation = "PENDING",
        BuildingSize = 0,
        OperatingArea = "PENDING",
        UserId = userId,
        Status = "ANALYSIS_IN_PROGRESS"
      };
      _context.Jobs.Add(tempJob);
      await _context.SaveChangesAsync();
      var jobId = tempJob.Id;

      // Step 2: Immediately run the first prompt to get the technical JSON data
      string initialAnalysisPromptKey;
      string systemPersonaPromptKey;

      switch (request.AnalysisType)
      {
        case "Renovation":
          initialAnalysisPromptKey = "renovation-00-initial-analysis.txt";
          systemPersonaPromptKey = "renovation-persona.txt";
          break;
        case "Selected":
          initialAnalysisPromptKey = "selected-prompt-system-persona.txt"; // This persona includes the JSON requirement
          systemPersonaPromptKey = "selected-prompt-system-persona.txt";
          break;
        default: // Comprehensive
          initialAnalysisPromptKey = "prompt-00-initial-analysis.txt";
          systemPersonaPromptKey = "system-persona.txt";
          break;
      }

      var initialAnalysisPrompt = await _promptManager.GetPromptAsync("", initialAnalysisPromptKey);
      var systemPersonaPrompt = await _promptManager.GetPromptAsync("", systemPersonaPromptKey);

      var (initialAiResponse, conversationId) = await _aiService.StartMultimodalConversationAsync(
          userId,
          request.DocumentUrls,
          systemPersonaPrompt,
          initialAnalysisPrompt
      );

      // Step 3: Immediately parse and save the technical details
      await _aiAnalysisService.ParseAndSaveAiJobDetails(jobId, initialAiResponse);

      // Step 4: Start the formal walkthrough session, now linked to a valid Job
      var walkthroughSession = await _walkthroughService.StartSessionAsync(jobId, userId, conversationId, initialAiResponse, request.AnalysisType, request.PromptKeys);

      // Step 5: Return the first step's data to the UI
      return Ok(new
      {
        jobId = jobId,
        sessionId = walkthroughSession.Id,
        firstStep = walkthroughSession.Steps.First()
      });
    }

    [HttpGet("walkthrough/{sessionId}")]
    public async Task<IActionResult> GetWalkthroughSession(Guid sessionId)
    {
      var session = await _walkthroughService.GetSessionAsync(sessionId);

      if (session == null)
      {
        return NotFound();
      }

      return Ok(session);
    }

    [HttpGet("walkthrough/{sessionId}/next")]
    public async Task<IActionResult> GetNextStep(Guid sessionId, [FromQuery] bool applyCostOptimisation = false)
    {
      var result = await _walkthroughService.GetNextStepAsync(sessionId, applyCostOptimisation);
      return Ok(result);
    }

    [HttpPost("walkthrough/{sessionId}/step/{stepIndex}/rerun")]
    public async Task<IActionResult> RerunStep(Guid sessionId, int stepIndex, [FromBody] RerunRequestDto data)
    {
      var result = await _walkthroughService.RerunStepAsync(sessionId, stepIndex, data);
      return Ok(result);
    }
  }
}

