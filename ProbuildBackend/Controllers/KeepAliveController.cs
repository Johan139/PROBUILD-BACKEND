using Microsoft.AspNetCore.Mvc;

namespace ProbuildBackend.Controllers
{
  [ApiController]
  [Route("[controller]")]
  public class KeepAliveController : ControllerBase
  {
    [HttpGet]
    public IActionResult Ping()
    {
      // This endpoint exists solely to receive a ping and keep the container alive.
      // It confirms the instance is active by returning a 200 OK
      return Ok("alive");
    }
  }
}