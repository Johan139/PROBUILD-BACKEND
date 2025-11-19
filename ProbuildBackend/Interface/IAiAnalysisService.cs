using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Interface
{
    public interface IAiAnalysisService
    {
        Task<string> PerformSelectedAnalysisAsync(string userId, AnalysisRequestDto requestDto, bool generateDetailsWithAi, string budgetLevel, string? conversationId = null, string? connectionId = null);
        Task<string> PerformComprehensiveAnalysisAsync(string userId, IEnumerable<string> documentUris, JobModel jobDetails, bool generateDetailsWithAi, string userContextText, string userContextFileUrl, string budgetLevel, string? connectionId = null, string promptKey = "prompt-00-initial-analysis.txt");
        Task<string> PerformRenovationAnalysisAsync(string userId, IEnumerable<string> documentUris, JobModel jobDetails, bool generateDetailsWithAi, string userContextText, string userContextFileUrl, string budgetLevel, string? connectionId = null, string promptKey = "renovation-00-initial-analysis.txt");
        Task<AnalysisResponseDto> PerformComparisonAnalysisAsync(ComparisonAnalysisRequestDto request, List<IFormFile> pdfFiles);
        Task<string> GenerateRebuttalAsync(string conversationId, string clientQuery);
        Task<string> GenerateRevisionAsync(string conversationId, string revisionRequest);
        Task<string> AnalyzeBidsAsync(List<BidModel> bids, string comparisonType);
        Task<string> GenerateFeedbackForUnsuccessfulBidderAsync(BidModel bid, BidModel winningBid);
        Task ParseAndSaveAiJobDetails(int jobId, string aiResponse);
    }
}