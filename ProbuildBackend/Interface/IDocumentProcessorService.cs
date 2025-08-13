using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProbuildBackend.Interface
{
    public interface IDocumentProcessorService
    {
        Task ProcessDocumentsForJobAsync(int jobId, List<string> documentUrls, string connectionId);
    }
}