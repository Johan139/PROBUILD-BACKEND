using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using ProbuildBackend.Interface;
using static ProbuildBackend.Services.DocumentProcessorService;
using ProbuildBackend.Models;
using ProbuildBackend.Options;

namespace ProbuildBackend.Services
{
    public class PdfImageConverter : IPdfImageConverter
    {
        private readonly OcrSettings _settings; 
        private readonly ApplicationDbContext _context; // Add database context

        public PdfImageConverter(OcrSettings settings, ApplicationDbContext context)
        {
            _settings = settings;
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<List<(int PageIndex, byte[] ImageBytes)>> ConvertPdfToImagesAsync(string blobUrl, Stream contentStream)
        {
            var pageImages = new List<(int, byte[])>();
            try
            {
                using var pdfReader = new PdfReader(contentStream);
                using var pdfDocument = new PdfDocument(pdfReader);

                for (int pageIndex = 0; pageIndex < pdfDocument.GetNumberOfPages(); pageIndex++)
                {
                    var page = pdfDocument.GetPage(pageIndex + 1); // iText7 pages are 1-based
                    var pageSize = page.GetPageSize();
                    int width = Math.Max((int)(pageSize.GetWidth() * _settings.Dpi / 72), 1);
                    int height = Math.Max((int)(pageSize.GetHeight() * _settings.Dpi / 72), 1);

                    // Create a blank image with ImageSharp
                    using var image = new Image<Rgba32>(width, height);

                    // Fill the image with a white background
                    image.Mutate(ctx => ctx.BackgroundColor(SixLabors.ImageSharp.Color.White));

                    // Render the PDF page to an image using PdfCanvasProcessor
                    // var strategy = new ImageRenderListener(image);
                    // var processor = new PdfCanvasProcessor(strategy);
                    // processor.ProcessPageContent(page);

                    // Resize the image using ImageSharp
                    int maxWidth = _settings.MaxImageWidth;
                    int maxHeight = _settings.MaxImageHeight;
                    image.Mutate(x =>
                    {
                        x.Resize(new ResizeOptions
                        {
                            Size = new Size(maxWidth, maxHeight),
                            Mode = ResizeMode.Max
                        });
                    });

                    // Save the image to a memory stream as PNG
                    using var memoryStream = new MemoryStream();
                    await image.SaveAsync(memoryStream, new PngEncoder());
                    pageImages.Add((pageIndex, memoryStream.ToArray()));
                }

                if (!pageImages.Any())
                    throw new InvalidOperationException($"No images were generated from PDF at {blobUrl}.");

                return pageImages;
            }
            catch (Exception ex)
            {
                //Log Error Occured
                var log = new DocumentProcessingLogModel()
                {
                    Location = "ConvertPdfToImagesAsync",
                    DateCreated = DateTime.UtcNow,
                    Description = "MESSAGE: " + ex.Message + " STACKTRACE: " + ex.StackTrace
                };
                _context.DocumentProcessingLog.Add(log);
                await _context.SaveChangesAsync();

                throw new InvalidOperationException($"Failed to convert PDF to images: {blobUrl}", ex);
            }
        }
    }
}
