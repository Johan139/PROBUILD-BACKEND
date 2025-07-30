using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;
using System.Linq;
using System.Threading.Tasks;

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
        public async Task<IActionResult> Analyze([FromForm] RenovationAnalysisRequest request, IFormFileCollection files)
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
