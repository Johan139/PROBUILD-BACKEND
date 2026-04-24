namespace BuildigBackend.Interface
{
    public interface IDocumentProcessorService
    {
        Task ProcessDocumentsForJobAsync(
            int jobId,
            List<string> documentUrls,
            string connectionId,
            bool generateDetailsWithAi,
            string userContextText,
            string userContextFileUrl,
            string budgetLevel,
           bool? isFrontEnd = false,
           string? ipAddress = null
        );
        Task ProcessSelectedAnalysisForJobAsync(
            int jobId,
            List<string> documentUrls,
            List<string> promptKeys,
            string connectionId,
            bool generateDetailsWithAi,
            string userContextText,
            string userContextFileUrl,
            string budgetLevel
        );
        Task ProcessRenovationAnalysisForJobAsync(
            int jobId,
            List<string> documentUrls,
            string connectionId,
            bool generateDetailsWithAi,
            string userContextText,
            string userContextFileUrl,
            string budgetLevel
        );
    }
}

