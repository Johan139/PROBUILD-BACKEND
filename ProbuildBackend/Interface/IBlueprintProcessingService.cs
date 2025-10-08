using ProbuildBackend.Models.DTO;
using System.Threading.Tasks;

public interface IBlueprintProcessingService
{
    Task<BlueprintAnalysisDto> ProcessBlueprintAsync(string userId, string pdfUrl, int jobId);
}