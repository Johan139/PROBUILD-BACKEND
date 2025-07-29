using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;
using System.Collections.Generic;
using System.Threading.Tasks;

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
        public async Task<IActionResult> Analyze([FromForm] ComparisonAnalysisRequest request, [FromForm] List<IFormFile> pdfFiles)
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