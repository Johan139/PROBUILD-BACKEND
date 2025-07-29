using ProbuildBackend.Models.DTO;
using System.Threading.Tasks;

namespace ProbuildBackend.Interface
{
    public interface IRenovationAnalysisService
    {
        Task<AnalysisResponse> PerformAnalysisAsync(RenovationAnalysisRequest request);
    }
}