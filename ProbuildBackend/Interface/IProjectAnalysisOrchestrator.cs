// ProbuildBackend/Interface/IProjectAnalysisOrchestrator.cs
using Microsoft.AspNetCore.Http;
using ProbuildBackend.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
public interface IProjectAnalysisOrchestrator
{
    Task<string> StartFullAnalysisAsync(string userId, IEnumerable<byte[]> blueprintImages, JobModel jobDetails);
    Task<string> GenerateRebuttalAsync(string conversationId, string clientQuery);
    Task<string> GenerateRevisionAsync(string conversationId, string revisionRequest);
}