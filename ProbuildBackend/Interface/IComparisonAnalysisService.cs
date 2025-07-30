using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Interface
{
    public interface IComparisonAnalysisService
    {
        Task<AnalysisResponse> PerformAnalysisAsync(ComparisonAnalysisRequest request, List<IFormFile> pdfFiles);
    }
}