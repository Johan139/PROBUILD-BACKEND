using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Interface
{
    public interface IRenovationAnalysisService
    {
        Task<AnalysisResponse> PerformAnalysisAsync(RenovationAnalysisRequest request);
    }
}