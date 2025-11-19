using PDFtoImage;
using SkiaSharp;
using ProbuildBackend.Interface;

namespace ProbuildBackend.Services
{
    public class PdfConversionService : IPdfConversionService
    {
        private readonly ILogger<PdfConversionService> _logger;

        public PdfConversionService(ILogger<PdfConversionService> logger)
        {
            _logger = logger;
        }

        public List<string> ConvertPdfToImages(Stream pdfStream, string outputFileNamePrefix, int dpi = 300)
        {
            var imagePaths = new List<string>();
            _logger.LogInformation("Starting PDF to image conversion with DPI {DPI}", dpi);

            try
            {
                // Reset stream position to the beginning before processing
                pdfStream.Position = 0;
                
                // Use Conversion.ToImages for multi-page PDFs which is more efficient
                var images = Conversion.ToImages(pdfStream);
                
                int pageNumber = 1;
                foreach (var image in images)
                {
                    var outputPath = Path.Combine(Path.GetTempPath(), $"{outputFileNamePrefix}_page_{pageNumber}.png");
                    using (var stream = File.Create(outputPath))
                    {
                        image.Encode(stream, SKEncodedImageFormat.Png, 100);
                    }
                    imagePaths.Add(outputPath);
                    _logger.LogInformation("Successfully rendered and saved page {PageNumber} to {Path}", pageNumber, outputPath);
                    pageNumber++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed during PDF to image conversion process.");
                throw;
            }

            return imagePaths;
        }
    }
}