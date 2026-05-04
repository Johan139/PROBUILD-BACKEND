using BuildigBackend.Models.DTO;
using BuildigBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildigBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReferralController : ControllerBase
{
    private readonly ReferralService _referralService;
    private readonly IConfiguration _configuration;

    public ReferralController(ReferralService referralService, IConfiguration configuration)
    {
        _referralService = referralService;
        _configuration = configuration;
    }

    [HttpGet("my-link/{userId}")]
    [Authorize]
    public async Task<ActionResult<ReferralLinkDto>> MyLink(string userId)
    {
        var link = await _referralService.GetOrCreateLinkForUserAsync(userId);
        var frontEnd =
            Environment.GetEnvironmentVariable("FRONTEND_URL")
            ?? _configuration["FrontEnd:FRONTEND_URL"]
            ?? "http://localhost:4200";
        return Ok(
            new ReferralLinkDto
            {
                Code = link.Code,
                Url = $"{frontEnd.TrimEnd('/')}/register?ref={Uri.EscapeDataString(link.Code)}",
            }
        );
    }

    [HttpGet("resolve/{code}")]
    [AllowAnonymous]
    public async Task<ActionResult<ReferralResolveDto>> Resolve(string code)
    {
        var link = await _referralService.ResolveActiveLinkAsync(code);
        if (link == null)
        {
            return Ok(new ReferralResolveDto { Valid = false, Message = "Invalid referral link." });
        }

        return Ok(new ReferralResolveDto { Valid = true, ReferrerUserId = link.ReferrerUserId });
    }
}
