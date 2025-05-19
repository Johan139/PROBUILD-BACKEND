using iText.Commons.Exceptions;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using System.Collections.Concurrent;

namespace ProbuildBackend.Services
{
    public class OcrTextExtractor : ITextExtractor
    {

        private readonly IPdfImageConverter _pdfImageConverter;
        private readonly IAiService _aiService;
        private readonly ApplicationDbContext _context; // Add database context
        public OcrTextExtractor(IPdfImageConverter pdfImageConverter, IAiService aiService, ApplicationDbContext context)
        {
            _pdfImageConverter = pdfImageConverter;
            _aiService = aiService;
            _context = context;
        }
        public async Task<string> ExtractTextWithOcr(string blobUrl, Stream contentStream, JobModel job )
        {
            try
            {

                //Console.WriteLine($"LD_LIBRARY_PATH: {Environment.GetEnvironmentVariable("LD_LIBRARY_PATH")}");
                //Console.WriteLine($"libpdfium.so exists: {File.Exists("/app/runtimes/linux-x64/native/libpdfium.so")}");
                //Console.WriteLine($"pdfium.dll symlink exists: {File.Exists("/app/runtimes/linux-x64/native/pdfium.dll")}");

                // Step 1: Convert PDF pages to images
                var pageImages = await _pdfImageConverter.ConvertPdfToImagesAsync(blobUrl, contentStream);

                // Step 2: Process pages in parallel
                var results = await ProcessPagesInParallelAsync(pageImages, blobUrl, job);

                // Step 3: Combine results
                return string.Join("\n", results);
            }
            catch (Exception ex)
            {
                //Log Error Occured
                var log = new DocumentProcessingLogModel()
                {
                    Location = "ExtractTextWithOcr",
                    DateCreated = DateTime.UtcNow,
                    Description = "MESSAGE: " + ex.Message + " STACKTRACE: " + ex.StackTrace
                };
                _context.DocumentProcessingLog.Add(log);
                await _context.SaveChangesAsync();

                throw new InvalidOperationException($"Failed OCR for blob: {blobUrl}", ex);
            }
        }
        public async Task<List<string>> ProcessPagesInParallelAsync(List<(int PageIndex, byte[] ImageBytes)> pageImages, string blobUrl, JobModel job)
        {
            // Process in batches of 2 pages
            const int batchSize = 2;
            var results = new ConcurrentBag<(int PageIndex, string Text)>();

            for (int i = 0; i < pageImages.Count; i += batchSize)
            {
                var batch = pageImages.Skip(i).Take(batchSize).ToList();
                var batchTasks = batch.Select(async page =>
                {
                    try
                    {
                        var text = await _aiService.AnalyzePageWithAssistantAsync(page.ImageBytes, page.PageIndex, blobUrl, job);
                        //var text = await _aiService.AnalyzePageWithAiAsync(page.ImageBytes, page.PageIndex, blobUrl);
                        results.Add((page.PageIndex, text));
                        Console.WriteLine($"✅ Page {page.PageIndex + 1}: Success - {text.Length} chars.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Page {page.PageIndex + 1} failed: {ex.Message}");
                        results.Add((page.PageIndex, $"[Page {page.PageIndex + 1}: OCR failed - {ex.Message}]"));
                    }
                });

                await Task.WhenAll(batchTasks);

                // Wait between batches to avoid rate limits
                if (i + batchSize < pageImages.Count)
                {
                    await Task.Delay(5000);
                }
            }

            return results.OrderBy(r => r.PageIndex).Select(r => r.Text).ToList();
        }
    }
}
