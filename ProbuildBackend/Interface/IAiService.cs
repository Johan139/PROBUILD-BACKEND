// ProbuildBackend/Interface/IAiService.cs
using ProbuildBackend.Models;

namespace ProbuildBackend.Interface
{
    public interface IAiService
    {
        Task<(string response, string conversationId)> ContinueConversationAsync(
            string? conversationId, string userId, string userPrompt, IEnumerable<byte[]>? imageBytesList);
        Task<string> AnalyzePageWithAssistantAsync(byte[] imageBytes, int pageIndex, string blobUrl, JobModel job);
        Task<string> RefineTextWithAiAsync(string extractedText, string blobUrl);
        Task<BillOfMaterials> GenerateBomFromText(string documentText);
        Task<string> PerformMultimodalAnalysisAsync(IEnumerable<string> fileUris, string prompt);

        Task<(string initialResponse, string conversationId)> StartMultimodalConversationAsync(string userId, IEnumerable<string> documentUris, string systemPersonaPrompt, string initialUserPrompt);
        Task<(string response, string conversationId)> StartTextConversationAsync(string userId, string systemPersonaPrompt, string initialUserPrompt);
    }
}
