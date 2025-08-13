using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Interface
{
    public interface IAnalysisService
    {
        Task<string> PerformAnalysisAsync(AnalysisRequestDto requestDto);
    }
}
