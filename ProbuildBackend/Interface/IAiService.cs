// ProbuildBackend/Interface/IAiService.cs
using ProbuildBackend.Models;

namespace ProbuildBackend.Interface
{
    public interface IAiService
    {
        Task<(string response, string conversationId)> ContinueConversationAsync(
            string? conversationId, string userId, string userPrompt, IEnumerable<string>? documentUris, bool isAnalysis = false);
        Task<string> AnalyzePageWithAssistantAsync(byte[] imageBytes, int pageIndex, string blobUrl, JobModel job);
        Task<string> RefineTextWithAiAsync(string extractedText, string blobUrl);
        Task<BillOfMaterials> GenerateBomFromText(string documentText);
        Task<string> PerformMultimodalAnalysisAsync(IEnumerable<string> fileUris, string prompt, bool isAnalysis = false);

        Task<(string initialResponse, string conversationId)> StartMultimodalConversationAsync(string userId, IEnumerable<string> documentUris, string systemPersonaPrompt, string initialUserPrompt, string? conversationId = null);
        Task<(string response, string conversationId)> StartTextConversationAsync(string userId, string systemPersonaPrompt, string initialUserPrompt, string? conversationId = null);
    }
}
