using System.IO;
using System.Threading.Tasks;

namespace ProbuildBackend.Interface
{
    public interface IPdfTextExtractionService
    {
        Task<string> ExtractTextAsync(Stream pdfStream);
    }
}