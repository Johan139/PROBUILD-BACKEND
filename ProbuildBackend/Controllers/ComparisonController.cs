using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ComparisonController : ControllerBase
    {
        private readonly IComparisonAnalysisService _comparisonAnalysisService;

        public ComparisonController(IComparisonAnalysisService comparisonAnalysisService)
        {
            _comparisonAnalysisService = comparisonAnalysisService;
        }

        [HttpPost("analyze")]
        public async Task<IActionResult> Analyze([FromForm] ComparisonAnalysisRequestDto request, [FromForm] List<IFormFile> pdfFiles)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _comparisonAnalysisService.PerformAnalysisAsync(request, pdfFiles);
            return Ok(response);
        }
    }
}