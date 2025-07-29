using Microsoft.AspNetCore.Http;
using ProbuildBackend.Models.DTO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProbuildBackend.Interface
{
    public interface IComparisonAnalysisService
    {
        Task<AnalysisResponse> PerformAnalysisAsync(ComparisonAnalysisRequest request, List<IFormFile> pdfFiles);
    }
}