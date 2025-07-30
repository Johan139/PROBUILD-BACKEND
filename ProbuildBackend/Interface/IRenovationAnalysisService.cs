using Microsoft.AspNetCore.Http;
using ProbuildBackend.Models.DTO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProbuildBackend.Interface
{
    public interface IRenovationAnalysisService
    {
        Task<AnalysisResponse> PerformAnalysisAsync(RenovationAnalysisRequest request, List<IFormFile> pdfFiles);
    }
}