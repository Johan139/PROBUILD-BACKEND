using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Interface
{
    public interface IRenovationAnalysisService
    {
        Task<AnalysisResponseDto> PerformAnalysisAsync(RenovationAnalysisRequestDto request, List<IFormFile> pdfFiles);
    }
}