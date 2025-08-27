namespace ProbuildBackend.Interface
{
    public interface IDocumentProcessorService
    {
        Task ProcessDocumentsForJobAsync(int jobId, List<string> documentUrls, string connectionId, bool generateDetailsWithAi, string userContextText, string userContextFileUrl);
        Task ProcessSelectedAnalysisForJobAsync(int jobId, List<string> documentUrls, List<string> promptKeys, string connectionId, bool generateDetailsWithAi, string userContextText, string userContextFileUrl);
        Task ProcessRenovationAnalysisForJobAsync(int jobId, List<string> documentUrls, string connectionId, bool generateDetailsWithAi, string userContextText, string userContextFileUrl);
     }
 }
