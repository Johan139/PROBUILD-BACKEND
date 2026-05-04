using BuildigBackend.Models;
using BuildigBackend.Models.DTO;
using BuildigBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildigBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PromotionalCampaignController : ControllerBase
{
    private readonly PromotionalCampaignService _promotionalCampaignService;

    public PromotionalCampaignController(PromotionalCampaignService promotionalCampaignService)
    {
        _promotionalCampaignService = promotionalCampaignService;
    }

    [HttpPost("track")]
    [AllowAnonymous]
    public async Task<IActionResult> Track(
        [FromBody] PromotionalCampaignTrackRequestDto request,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.EventType))
            return BadRequest("Code and EventType are required.");

        var saved = await _promotionalCampaignService.TrackEventByCodeAsync(
            request.Code,
            request.EventType,
            request.Email,
            request.UserId,
            request.StripeSubscriptionId,
            request.RevenueAmount,
            request.Currency,
            cancellationToken
        );

        if (saved == null)
            return BadRequest("Invalid or expired promotional link.");

        return Ok();
    }

    /// <summary>Landing pages and apps resolve a promo slug for messaging and validation.</summary>
    [HttpGet("resolve/{code}")]
    [AllowAnonymous]
    public async Task<ActionResult<PromotionalLinkResolveResponseDto>> Resolve(
        string code,
        CancellationToken cancellationToken
    )
    {
        var link = await _promotionalCampaignService.TryResolveActiveLinkAsync(
            code,
            cancellationToken
        );

        if (link == null)
        {
            return Ok(
                new PromotionalLinkResolveResponseDto
                {
                    Valid = false,
                    Message = "This promotional link is invalid or has expired.",
                }
            );
        }

        return Ok(
            new PromotionalLinkResolveResponseDto
            {
                Valid = true,
                CampaignKind = link.CampaignKind.ToString(),
                Month1DiscountPercent = link.Month1DiscountPercent,
                Month2DiscountPercent = link.Month2DiscountPercent,
                ExpiresAtUtc = link.ExpiresAtUtc,
                RepLabel = link.RepLabel,
                AllowedPackageName = link.AllowedPackageName,
                AllowedBillingCycle = link.AllowedBillingCycle,
            }
        );
    }
}
