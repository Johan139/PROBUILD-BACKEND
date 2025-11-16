using ProbuildBackend.Models.DTO;

public interface IBlueprintProcessingService
{
    Task<BlueprintAnalysisDto> ProcessBlueprintAsync(string userId, string pdfUrl, int jobId);
}