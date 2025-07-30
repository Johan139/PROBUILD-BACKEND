using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;
using System.Text;

namespace ProbuildBackend.Services
{
    public class ComparisonAnalysisService : IComparisonAnalysisService
    {
        private readonly IAiService _aiService;
        private readonly IPromptManagerService _promptManager;
        private readonly IPdfTextExtractionService _pdfTextExtractionService;

        public ComparisonAnalysisService(IAiService aiService, IPromptManagerService promptManager, IPdfTextExtractionService pdfTextExtractionService)
        {
            _aiService = aiService;
            _promptManager = promptManager;
            _pdfTextExtractionService = pdfTextExtractionService;
        }

        public async Task<AnalysisResponse> PerformAnalysisAsync(ComparisonAnalysisRequest request, List<IFormFile> pdfFiles)
        {
            string promptFileName = request.ComparisonType switch
            {
                ComparisonType.Vendor => "vendor-comparison-prompt.pdf",
                ComparisonType.Subcontractor => "subcontractor-comparison-prompt.pdf",
                _ => throw new System.ArgumentException("Invalid comparison type")
            };

            var prompt = await _promptManager.GetPromptAsync("ComparisonPrompts/", promptFileName);

            var combinedPdfText = new StringBuilder();
            foreach (var pdfFile in pdfFiles)
            {
                using var memoryStream = new MemoryStream();
                await pdfFile.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                var pdfText = await _pdfTextExtractionService.ExtractTextAsync(memoryStream);
                combinedPdfText.AppendLine(pdfText);
            }

            var fullPrompt = $"{prompt}\n\n{combinedPdfText}";

            var (analysisResult, conversationId) = await _aiService.StartMultimodalConversationAsync(request.UserId, null, fullPrompt, "Analyze the document based on the provided details.");

            return new AnalysisResponse
            {
                AnalysisResult = analysisResult,
                ConversationId = conversationId
            };
        }
    }
}