// ProbuildBackend/Interface/IProjectAnalysisOrchestrator.cs
using ProbuildBackend.Models;
public interface IProjectAnalysisOrchestrator
{
    Task<string> StartFullAnalysisAsync(string userId, IEnumerable<byte[]> blueprintImages, JobModel jobDetails);
    Task<string> GenerateRebuttalAsync(string conversationId, string clientQuery);
    Task<string> GenerateRevisionAsync(string conversationId, string revisionRequest);
}