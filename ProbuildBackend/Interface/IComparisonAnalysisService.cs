using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Interface
{
    public interface IComparisonAnalysisService
    {
        Task<AnalysisResponseDto> PerformAnalysisAsync(ComparisonAnalysisRequestDto request, List<IFormFile> pdfFiles);
    }
}