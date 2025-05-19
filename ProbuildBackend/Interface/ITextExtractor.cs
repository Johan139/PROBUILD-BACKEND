using ProbuildBackend.Models;

namespace ProbuildBackend.Interface
{
    public interface ITextExtractor
    {
        Task<string> ExtractTextWithOcr(string blobUrl, Stream pdfStream, JobModel job);

        Task<List<string>> ProcessPagesInParallelAsync(List<(int PageIndex, byte[] ImageBytes)> pageImages, string blobUrl, JobModel job);
    }
}
