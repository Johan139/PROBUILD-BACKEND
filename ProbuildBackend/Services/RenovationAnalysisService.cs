using Microsoft.AspNetCore.Http;
using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ProbuildBackend.Services
{
    public class RenovationAnalysisService : IRenovationAnalysisService
    {
        private readonly IAiService _aiService;
        private readonly IPromptManagerService _promptManager;
        private readonly IPdfTextExtractionService _pdfTextExtractionService;

        public RenovationAnalysisService(IAiService aiService, IPromptManagerService promptManager, IPdfTextExtractionService pdfTextExtractionService)
        {
            _aiService = aiService;
            _promptManager = promptManager;
            _pdfTextExtractionService = pdfTextExtractionService;
        }

        public async Task<AnalysisResponse> PerformAnalysisAsync(RenovationAnalysisRequest request, List<IFormFile> pdfFiles)
        {
            var prompt = await _promptManager.GetPromptAsync("RenovationPrompts/", "ProBuildAI_Renovation_Prompt.txt");

            var combinedPdfText = new StringBuilder();
            if (pdfFiles != null)
            {
                foreach (var pdfFile in pdfFiles)
                {
                    using var memoryStream = new MemoryStream();
                    await pdfFile.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;
                    var pdfText = await _pdfTextExtractionService.ExtractTextAsync(memoryStream);
                    combinedPdfText.AppendLine(pdfText);
                }
            }

            var fullPrompt = $"{prompt}\n\n{combinedPdfText}";

            var (analysisResult, conversationId) = await _aiService.StartMultimodalConversationAsync(request.UserId, null, fullPrompt, "Analyze the renovation project based on the provided details.");

            return new AnalysisResponse
            {
                AnalysisResult = analysisResult,
                ConversationId = conversationId
            };
        }
    }
}
