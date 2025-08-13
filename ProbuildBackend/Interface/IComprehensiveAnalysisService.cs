using ProbuildBackend.Models;

namespace ProbuildBackend.Interface
{
    public interface IComprehensiveAnalysisService
    {
        Task<string> PerformAnalysisFromTextAsync(string userId, string fullText, JobModel jobDetails);
        Task<string> PerformAnalysisFromImagesAsync(string userId, IEnumerable<byte[]> blueprintImages, JobModel jobDetails);
        Task<string> PerformAnalysisFromFilesAsync(string userId, IEnumerable<string> documentUris, JobModel jobDetails, string promptKey = "prompt-00-initial-analysis.txt");
    }
}
