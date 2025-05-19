using ProbuildBackend.Models;
using ProbuildBackend.Services;
using static ProbuildBackend.Services.DocumentProcessorService;

namespace ProbuildBackend.Interface
{
    public interface IAiService
    {
        Task<string> AnalyzePageWithAiAsync(byte[] imageBytes, int pageIndex, string blobUrl);
        Task<string> AnalyzePageWithAssistantAsync(byte[] imageBytes, int pageIndex, string blobUrl,JobModel job);
        Task<string> RefineTextWithAiAsync(string extractedText, string blobUrl);
        Task<BillOfMaterials> GenerateBomFromText(string documentText);
    }
}
