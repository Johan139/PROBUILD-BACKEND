using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models.DTO;

[ApiController]
[Route("api/[controller]")]
public class BlueprintsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public BlueprintsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("by-job/{jobId}")]
    public async Task<ActionResult<BlueprintAnalysisDto>> GetBlueprintForJob(int jobId)
    {
        var blueprint = await _context.BlueprintAnalyses.FirstOrDefaultAsync(b => b.JobId == jobId);

        if (blueprint == null)
            return NotFound();

        if (blueprint.JobId == null)
        {
            return BadRequest(new { message = "Blueprint is not associated with a job." });
        }

        var dto = new BlueprintAnalysisDto
        {
            Id = blueprint.Id,
            JobId = blueprint.JobId.Value,
            Name = blueprint.OriginalFileName,
            PdfUrl = blueprint.PdfUrl,
            PageImageUrls =
                JsonSerializer.Deserialize<List<string>>(blueprint.PageImageUrlsJson)
                ?? new List<string>(),
            AnalysisJson = blueprint.AnalysisJson,
            TotalPages = blueprint.TotalPages,
        };

        return Ok(dto);
    }
}
