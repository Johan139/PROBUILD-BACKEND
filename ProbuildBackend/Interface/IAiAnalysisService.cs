using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Interface
{
    public interface IAiAnalysisService
    {
        Task<Conversation> PerformSelectedAnalysisAsync(string userId, AnalysisRequestDto requestDto, bool generateDetailsWithAi);
        Task<string> PerformComprehensiveAnalysisAsync(string userId, IEnumerable<string> documentUris, JobModel jobDetails, bool generateDetailsWithAi, string userContextText, string userContextFileUrl, string promptKey = "prompt-00-initial-analysis.txt");
        Task<AnalysisResponseDto> PerformRenovationAnalysisAsync(RenovationAnalysisRequestDto request, List<IFormFile> pdfFiles);
        Task<AnalysisResponseDto> PerformComparisonAnalysisAsync(ComparisonAnalysisRequestDto request, List<IFormFile> pdfFiles);
        Task<string> GenerateRebuttalAsync(string conversationId, string clientQuery);
        Task<string> GenerateRevisionAsync(string conversationId, string revisionRequestText);
    }
}
