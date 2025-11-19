using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Services
{
  public class WalkthroughService : IWalkthroughService
  {
    private readonly ApplicationDbContext _context;
    private readonly GeminiAiService _aiService;
    private readonly PromptManagerService _promptManager;
    public WalkthroughService(ApplicationDbContext context, GeminiAiService aiService, PromptManagerService promptManager)
    {
      _context = context;
      _aiService = aiService;
      _promptManager = promptManager;
    }

    public async Task<WalkthroughSession> StartSessionAsync(int jobId, string userId, string conversationId, string initialAiResponse, string analysisType, List<string> promptKeys = null)
    {
      var promptSequence = GetPromptSequence(analysisType, promptKeys);

      var session = new WalkthroughSession
      {
        JobId = jobId,
        UserId = userId,
        CreatedAt = DateTime.UtcNow,
        AnalysisType = analysisType,
        PromptSequenceJson = System.Text.Json.JsonSerializer.Serialize(promptSequence)
      };

      _context.WalkthroughSessions.Add(session);
      await _context.SaveChangesAsync();

      var step = new WalkthroughStep
      {
        SessionId = session.Id,
        StepIndex = 0,
        PromptKey = "prompt-00-initial-analysis.txt",
        AiResponse = initialAiResponse,
        Timestamp = DateTime.UtcNow,
        ConversationId = conversationId
      };

      _context.WalkthroughSteps.Add(step);
      await _context.SaveChangesAsync();

      session.Steps = new List<WalkthroughStep> { step };

      return session;
    }

    private List<string> GetPromptSequence(string analysisType, List<string> selectedPrompts)
    {
      switch (analysisType)
      {
        case "Comprehensive":
        case "sequential":
          return new List<string> {
                    "prompt-01-site-logistics.txt", "prompt-02-quality-management.txt", "prompt-03-demolition.txt",
                    "prompt-04-groundwork.txt", "prompt-05-framing.txt", "prompt-06-roofing.txt",
                    "prompt-07-exterior.txt", "prompt-08-electrical.txt", "prompt-09-plumbing.txt",
                    "prompt-10-hvac.txt", "prompt-11-fire-protection.txt", "prompt-12-insulation.txt",
                    "prompt-13-drywall.txt", "prompt-14-painting.txt", "prompt-15-trim.txt",
                    "prompt-16-kitchen-bath.txt", "prompt-17-flooring.txt", "prompt-18-exterior-flatwork.txt",
                    "prompt-19-cleaning.txt", "prompt-20-risk-analyst.txt", "prompt-21-timeline.txt",
                    "prompt-22-general-conditions.txt", "prompt-23-procurement.txt", "prompt-24-daily-construction-plan.txt",
                    "prompt-25-cost-breakdowns.txt", "prompt-26-value-engineering.txt", "prompt-27-environmental-lifecycle.txt",
                    "prompt-28-project-closeout.txt", "executive-summary-prompt.txt"
                };
        case "Selected":
          return selectedPrompts ?? new List<string>();
        case "Renovation":
          return new List<string> {
                   "renovation-01-demolition.txt", "renovation-02-structural-alterations.txt", "renovation-03-rough-in-mep.txt",
                   "renovation-04-insulation-drywall.txt", "renovation-05-interior-finishes.txt", "renovation-06-fixtures-fittings-equipment.txt",
                   "renovation-07-cost-breakdown-summary.txt", "renovation-08-project-timeline.txt", "renovation-09-environmental-impact.txt",
                   "renovation-10-final-review-rebuttal.txt", "executive-summary-prompt.txt"
                };
        default:
          throw new ArgumentException("Invalid analysis type provided.", nameof(analysisType));
      }
    }
    public async Task<WalkthroughStep> GetNextStepAsync(Guid sessionId, bool applyCostOptimisation = false)
    {
      var session = await _context.WalkthroughSessions.FindAsync(sessionId);
      if (session == null)
      {
        throw new Exception("Session not found.");
      }

      var promptNames = System.Text.Json.JsonSerializer.Deserialize<List<string>>(session.PromptSequenceJson);

      var lastStep = await _context.WalkthroughSteps
          .Where(s => s.SessionId == sessionId)
          .OrderByDescending(s => s.StepIndex)
          .FirstOrDefaultAsync();

      if (lastStep == null)
      {
        throw new Exception("Session not found or has no steps.");
      }

      var nextStepIndex = lastStep.StepIndex + 1;
      if (nextStepIndex >= promptNames.Count)
      {
        throw new Exception("Walkthrough complete.");
      }

      var nextPromptKey = promptNames[nextStepIndex];
      var nextPrompt = await _promptManager.GetPromptAsync("prompts", nextPromptKey);

      if (applyCostOptimisation)
      {
        var costOptimisationPrompt = await _promptManager.GetPromptAsync("prompts", "prompt-26-value-engineering.txt");
        nextPrompt = $"{nextPrompt}\n\n{costOptimisationPrompt}";
      }

      var (aiResponse, conversationId) = await _aiService.ContinueConversationAsync(lastStep.ConversationId, null, nextPrompt, null);

      var nextStep = new WalkthroughStep
      {
        SessionId = sessionId,
        StepIndex = nextStepIndex,
        PromptKey = nextPromptKey,
        AiResponse = aiResponse,
        Timestamp = DateTime.UtcNow,
        ConversationId = conversationId
      };

      _context.WalkthroughSteps.Add(nextStep);
      await _context.SaveChangesAsync();

      return nextStep;
    }

    public async Task<WalkthroughStep> RerunStepAsync(Guid sessionId, int stepIndex, RerunRequestDto data)
    {
      var currentStep = await _context.WalkthroughSteps
          .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.StepIndex == stepIndex);

      if (currentStep == null)
      {
        throw new Exception("Step not found.");
      }

      var originalPromptText = await _promptManager.GetPromptAsync("prompts", currentStep.PromptKey);

      var metaPrompt = new System.Text.StringBuilder();
      metaPrompt.AppendLine("You are an AI assistant tasked with refining a construction analysis based on user feedback.");
      metaPrompt.AppendLine("Your original task was to respond to the following prompt:");
      metaPrompt.AppendLine("--- ORIGINAL PROMPT ---");
      metaPrompt.AppendLine(originalPromptText);
      metaPrompt.AppendLine("--- END ORIGINAL PROMPT ---");
      metaPrompt.AppendLine("\nYour original response to this prompt was:");
      metaPrompt.AppendLine("--- ORIGINAL AI RESPONSE ---");
      metaPrompt.AppendLine(data.OriginalAiResponse);
      metaPrompt.AppendLine("--- END ORIGINAL AI RESPONSE ---");

      metaPrompt.AppendLine("\nThe user has now provided an edited version of your response. This edited version should be treated as the new source of truth. Any sections they have modified are considered 'locked' and must be preserved exactly as they wrote them.");
      metaPrompt.AppendLine("--- USER'S EDITED VERSION (SOURCE OF TRUTH) ---");
      metaPrompt.AppendLine(data.UserEditedResponse);
      metaPrompt.AppendLine("--- END USER'S EDITED VERSION ---");

      if (!string.IsNullOrWhiteSpace(data.UserComments))
      {
        metaPrompt.AppendLine("\nAdditionally, the user has provided the following general comments. You must address these comments in your new response, focusing your changes on the areas they've highlighted, while still preserving their locked edits.");
        metaPrompt.AppendLine("--- USER'S GENERAL COMMENTS ---");
        metaPrompt.AppendLine(data.UserComments);
        metaPrompt.AppendLine("--- END USER'S GENERAL COMMENTS ---");
      }

      if (data.ApplyCostOptimisation)
      {
        var costOptimisationPrompt = await _promptManager.GetPromptAsync("prompts", "prompt-26-value-engineering.txt");
        metaPrompt.AppendLine("\n--- COST OPTIMISATION ---");
        metaPrompt.AppendLine(costOptimisationPrompt);
        metaPrompt.AppendLine("--- END COST OPTIMISATION ---");
      }

      metaPrompt.AppendLine("\nYour task is to now generate a new, complete response to the ORIGINAL PROMPT.");
      metaPrompt.AppendLine("This new response must:");
      metaPrompt.AppendLine("1. Integrate all of the user's inline edits from their 'EDITED VERSION' exactly as they appear.");
      metaPrompt.AppendLine("2. Address the points raised in the 'GENERAL COMMENTS'.");
      metaPrompt.AppendLine("3. Recalculate or adjust any other parts of the original response that are impacted by the user's changes, but do not alter the locked sections.");
      metaPrompt.AppendLine("4. Present the final output as a complete, clean response without mentioning this feedback process.");

      var (newAiResponse, _) = await _aiService.ContinueConversationAsync(currentStep.ConversationId, null, metaPrompt.ToString(), null);

      currentStep.AiResponse = newAiResponse;
      currentStep.UserEditedResponse = data.UserEditedResponse;
      currentStep.UserComments = data.UserComments;
      currentStep.Timestamp = DateTime.UtcNow;

      await _context.SaveChangesAsync();

      return currentStep;
    }

    public async Task<WalkthroughSession> GetSessionAsync(Guid sessionId)
    {
      var session = await _context.WalkthroughSessions
          .Include(s => s.Steps.OrderBy(step => step.StepIndex))
          .FirstOrDefaultAsync(s => s.Id == sessionId);

      return session;
    }
  }
}
