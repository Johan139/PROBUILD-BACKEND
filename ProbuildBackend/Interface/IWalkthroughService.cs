using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Interface
{
  public interface IWalkthroughService
  {
    Task<WalkthroughSession> StartSessionAsync(int jobId, string userId, string conversationId, string initialAiResponse, string analysisType, List<string> promptKeys = null);
    Task<WalkthroughStep> GetNextStepAsync(Guid sessionId, bool applyCostOptimisation = false);
    Task<WalkthroughStep> RerunStepAsync(Guid sessionId, int stepIndex, RerunRequestDto data);
    Task<WalkthroughSession> GetSessionAsync(Guid sessionId);
  }
}