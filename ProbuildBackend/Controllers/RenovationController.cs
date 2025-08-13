using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RenovationController : ControllerBase
    {
        private readonly IRenovationAnalysisService _renovationAnalysisService;

        public RenovationController(IRenovationAnalysisService renovationAnalysisService)
        {
            _renovationAnalysisService = renovationAnalysisService;
        }

        [HttpPost("analyze")]
        public async Task<IActionResult> Analyze([FromForm] RenovationAnalysisRequestDto request, IFormFileCollection files)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _renovationAnalysisService.PerformAnalysisAsync(request, files.ToList());
            return Ok(response);
        }
    }
}
