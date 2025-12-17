using System.Text;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using ProbuildBackend.Interface;

namespace ProbuildBackend.Services
{
    public class PdfTextExtractionService : IPdfTextExtractionService
    {
        public Task<string> ExtractTextAsync(Stream pdfStream)
        {
            var text = new StringBuilder();
            using (var pdfReader = new PdfReader(pdfStream))
            using (var pdfDocument = new PdfDocument(pdfReader))
            {
                for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
                {
                    var strategy = new LocationTextExtractionStrategy();
                    var page = pdfDocument.GetPage(i);
                    var processor = new PdfCanvasProcessor(strategy);
                    processor.ProcessPageContent(page);
                    text.Append(strategy.GetResultantText());
                }
            }
            return Task.FromResult(text.ToString());
        }
    }
}
