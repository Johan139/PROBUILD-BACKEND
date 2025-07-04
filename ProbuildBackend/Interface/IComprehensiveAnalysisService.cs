using ProbuildBackend.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProbuildBackend.Interface
{
    public interface IComprehensiveAnalysisService
    {
        Task<string> PerformAnalysisFromTextAsync(string userId, string fullText, JobModel jobDetails);
        Task<string> PerformAnalysisFromImagesAsync(string userId, IEnumerable<byte[]> blueprintImages, JobModel jobDetails);
        Task<string> PerformAnalysisFromFilesAsync(IEnumerable<string> documentUris, string initialPrompt);
    }
}