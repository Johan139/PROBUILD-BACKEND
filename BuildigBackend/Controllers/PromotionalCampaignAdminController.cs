using System.Collections.Generic;
using System.Security.Cryptography;
using BuildigBackend.Models;
using BuildigBackend.Models.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildigBackend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PromotionalCampaignAdminController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public PromotionalCampaignAdminController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<PromotionalCampaignLinkAdminDto>>> List()
    {
        try
        {

 
        var items = await _context.PromotionalCampaignLinks
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new PromotionalCampaignLinkAdminDto
            {
                Id = x.Id,
                Code = x.Code,
                PublicCode = x.PublicCode,
                CampaignKind = x.CampaignKind,
                RepLabel = x.RepLabel,
                RepUserId = x.RepUserId,
                Month1DiscountPercent = x.Month1DiscountPercent,
                Month2DiscountPercent = x.Month2DiscountPercent,
                ExpiresAtUtc = x.ExpiresAtUtc,
                IsActive = x.IsActive,
                MaxRedemptions = x.MaxRedemptions,
                CurrentRedemptionCount = x.CurrentRedemptionCount,
                StripeCouponId = x.StripeCouponId,
                AllowedPackageName = x.AllowedPackageName,
                AllowedBillingCycle = x.AllowedBillingCycle,
            })
            .ToListAsync();

        return Ok(items);
        }
        catch (Exception ex)
        {

            throw;
        }
    }

    [HttpPost("upsert")]
    public async Task<ActionResult<PromotionalCampaignLinkAdminDto>> Upsert(
        [FromBody] PromotionalCampaignLinkUpsertRequestDto request
    )
    {
        if (request == null)
            return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest("Code is required.");

        var normalizedCode = request.Code.Trim().ToUpperInvariant();

        PromotionalCampaignLink? entity = null;
        if (request.Id.HasValue && request.Id.Value > 0)
        {
            entity = await _context.PromotionalCampaignLinks
                .FirstOrDefaultAsync(x => x.Id == request.Id.Value);
        }

        entity ??= await _context.PromotionalCampaignLinks
            .FirstOrDefaultAsync(x => x.Code == normalizedCode);

        if (entity == null)
        {
            entity = new PromotionalCampaignLink();
            entity.Code = normalizedCode;
            _context.PromotionalCampaignLinks.Add(entity);
        }

        entity.CampaignKind = request.CampaignKind;
        entity.PublicCode = string.IsNullOrWhiteSpace(request.PublicCode)
            ? (string.IsNullOrWhiteSpace(entity.PublicCode)
                ? await GenerateUniquePublicCodeAsync()
                : entity.PublicCode)
            : request.PublicCode.Trim().ToUpperInvariant();
        entity.RepLabel = request.RepLabel;
        entity.RepUserId = request.RepUserId;
        entity.Month1DiscountPercent = request.Month1DiscountPercent;
        entity.Month2DiscountPercent = request.Month2DiscountPercent;
        entity.ExpiresAtUtc = request.ExpiresAtUtc;
        entity.IsActive = request.IsActive;
        entity.MaxRedemptions = request.MaxRedemptions;
        entity.StripeCouponId = request.StripeCouponId;
        entity.AllowedPackageName = request.AllowedPackageName;
        entity.AllowedBillingCycle = request.AllowedBillingCycle;

        await _context.SaveChangesAsync();

        return Ok(
            new PromotionalCampaignLinkAdminDto
            {
                Id = entity.Id,
                Code = entity.Code,
                PublicCode = entity.PublicCode,
                CampaignKind = entity.CampaignKind,
                RepLabel = entity.RepLabel,
                RepUserId = entity.RepUserId,
                Month1DiscountPercent = entity.Month1DiscountPercent,
                Month2DiscountPercent = entity.Month2DiscountPercent,
                ExpiresAtUtc = entity.ExpiresAtUtc,
                IsActive = entity.IsActive,
                MaxRedemptions = entity.MaxRedemptions,
                CurrentRedemptionCount = entity.CurrentRedemptionCount,
                StripeCouponId = entity.StripeCouponId,
                AllowedPackageName = entity.AllowedPackageName,
                AllowedBillingCycle = entity.AllowedBillingCycle,
            }
        );
    }

    [HttpPost("bulk-generate")]
    public async Task<ActionResult<List<PromotionalCampaignLinkAdminDto>>> BulkGenerate(
        [FromBody] PromotionalCampaignBulkGenerateRequestDto request
    )
    {
        if (request.Count <= 0 || request.Count > 500)
            return BadRequest("Count must be between 1 and 500.");

        var prefix = (request.Prefix ?? "PROMO").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(prefix))
            prefix = "PROMO";

        var created = new List<PromotionalCampaignLinkAdminDto>();

        for (var i = 0; i < request.Count; i++)
        {
            var serial = request.StartNumber + i;
            var code = $"{prefix}-{serial:000}";
            var exists = await _context.PromotionalCampaignLinks.AnyAsync(x => x.Code == code);
            if (exists)
                continue;

            var entity = new PromotionalCampaignLink
            {
                Code = code,
                PublicCode = await GenerateUniquePublicCodeAsync(),
                CampaignKind = request.CampaignKind,
                RepLabel = request.RepLabel,
                RepUserId = request.RepUserId,
                Month1DiscountPercent = request.Month1DiscountPercent,
                Month2DiscountPercent = request.Month2DiscountPercent,
                ExpiresAtUtc = request.ExpiresAtUtc,
                IsActive = request.IsActive,
                MaxRedemptions = request.MaxRedemptions,
                AllowedPackageName = request.AllowedPackageName,
                AllowedBillingCycle = request.AllowedBillingCycle,
            };

            _context.PromotionalCampaignLinks.Add(entity);
            created.Add(
                new PromotionalCampaignLinkAdminDto
                {
                    Id = entity.Id,
                    Code = entity.Code,
                    PublicCode = entity.PublicCode,
                    CampaignKind = entity.CampaignKind,
                    RepLabel = entity.RepLabel,
                    RepUserId = entity.RepUserId,
                    Month1DiscountPercent = entity.Month1DiscountPercent,
                    Month2DiscountPercent = entity.Month2DiscountPercent,
                    ExpiresAtUtc = entity.ExpiresAtUtc,
                    IsActive = entity.IsActive,
                    MaxRedemptions = entity.MaxRedemptions,
                    CurrentRedemptionCount = entity.CurrentRedemptionCount,
                    StripeCouponId = entity.StripeCouponId,
                    AllowedPackageName = entity.AllowedPackageName,
                    AllowedBillingCycle = entity.AllowedBillingCycle,
                }
            );
        }

        await _context.SaveChangesAsync();
        return Ok(created);
    }

    [HttpPost("{id:int}/toggle-active")]
    public async Task<IActionResult> ToggleActive(int id, [FromBody] bool isActive)
    {
        var entity = await _context.PromotionalCampaignLinks.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound();

        entity.IsActive = isActive;
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("events")]
    public async Task<ActionResult<List<PromotionalCampaignEventAdminDto>>> Events(
        [FromQuery] int take = 500
    )
    {
        var safeTake = Math.Clamp(take, 1, 2000);

        var items = await _context.PromotionalCampaignEvents
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(safeTake)
            .Select(x => new PromotionalCampaignEventAdminDto
            {
                Id = x.Id,
                PromotionalCampaignLinkId = x.PromotionalCampaignLinkId,
                PromotionalCode = x.PromotionalCampaignLink.Code,
                PromotionalPublicCode = x.PromotionalCampaignLink.PublicCode,
                CampaignKind = x.PromotionalCampaignLink.CampaignKind.ToString(),
                RepLabel = x.PromotionalCampaignLink.RepLabel,
                EventType = x.EventType,
                Email = x.Email,
                UserId = x.UserId,
                StripeSubscriptionId = x.StripeSubscriptionId,
                RevenueAmount = x.RevenueAmount,
                Currency = x.Currency,
                CreatedAtUtc = x.CreatedAtUtc,
            })
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("performance")]
    public async Task<ActionResult<PromotionalCampaignPerformanceResponseDto>> Performance(
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null
    )
    {
        var links = await _context.PromotionalCampaignLinks
            .AsNoTracking()
            .ToListAsync();

        var eventsQuery = _context.PromotionalCampaignEvents
            .AsNoTracking()
            .Where(x => !fromUtc.HasValue || x.CreatedAtUtc >= fromUtc.Value)
            .Where(x => !toUtc.HasValue || x.CreatedAtUtc <= toUtc.Value);

        var events = await eventsQuery
            .Select(x => new PerformanceEventRow
            {
                PromotionalCampaignLinkId = x.PromotionalCampaignLinkId,
                EventType = x.EventType,
                RevenueAmount = x.RevenueAmount,
                CreatedAtUtc = x.CreatedAtUtc,
            })
            .ToListAsync();

        var groupedByLink = events
            .GroupBy(x => x.PromotionalCampaignLinkId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var byLink = links
            .Select(link =>
            {
                groupedByLink.TryGetValue(link.Id, out var linkEvents);
                linkEvents ??= new List<PerformanceEventRow>();
                var couponDay1To3 = 0;
                var couponDay4To7 = 0;
                var couponDay8To14 = 0;

                if (link.CampaignKind == PromotionalCampaignKind.ExpoCoupon && linkEvents != null)
                {
                    foreach (var evt in linkEvents.Where(x =>
                        string.Equals(x.EventType, "converted", StringComparison.OrdinalIgnoreCase)
                    ))
                    {
                        var day = (int)Math.Floor((evt.CreatedAtUtc - link.CreatedAtUtc).TotalDays) + 1;
                        if (day >= 1 && day <= 3)
                            couponDay1To3++;
                        else if (day >= 4 && day <= 7)
                            couponDay4To7++;
                        else if (day >= 8 && day <= 14)
                            couponDay8To14++;
                    }
                }

                return new PromotionalCampaignPerformanceByLinkDto
                {
                    PromotionalCampaignLinkId = link.Id,
                    Code = link.Code,
                    PublicCode = link.PublicCode,
                    CampaignKind = link.CampaignKind.ToString(),
                    RepLabel = link.RepLabel,
                    Visits = linkEvents.Count(x =>
                        string.Equals(x.EventType, "visit", StringComparison.OrdinalIgnoreCase)
                    ),
                    EmailCaptured = linkEvents.Count(x =>
                        string.Equals(x.EventType, "email_captured", StringComparison.OrdinalIgnoreCase)
                    ),
                    Signups = linkEvents.Count(x =>
                        string.Equals(x.EventType, "signup_completed", StringComparison.OrdinalIgnoreCase)
                    ),
                    CheckoutStarted = linkEvents.Count(x =>
                        string.Equals(x.EventType, "checkout_started", StringComparison.OrdinalIgnoreCase)
                    ),
                    Conversions = linkEvents.Count(x =>
                        string.Equals(x.EventType, "converted", StringComparison.OrdinalIgnoreCase)
                    ),
                    Revenue = linkEvents
                        .Where(x => string.Equals(x.EventType, "converted", StringComparison.OrdinalIgnoreCase))
                        .Sum(x => x.RevenueAmount) ?? 0m,
                    CouponConvertedDay1To3 = couponDay1To3,
                    CouponConvertedDay4To7 = couponDay4To7,
                    CouponConvertedDay8To14 = couponDay8To14,
                };
            })
            .OrderByDescending(x => x.Revenue)
            .ThenByDescending(x => x.Conversions)
            .ToList();

        var byRep = byLink
            .GroupBy(x => string.IsNullOrWhiteSpace(x.RepLabel) ? "Unassigned" : x.RepLabel!.Trim())
            .Select(g => new PromotionalCampaignPerformanceByRepDto
            {
                RepLabel = g.Key,
                LinkCount = g.Count(),
                Visits = g.Sum(x => x.Visits),
                EmailCaptured = g.Sum(x => x.EmailCaptured),
                Signups = g.Sum(x => x.Signups),
                CheckoutStarted = g.Sum(x => x.CheckoutStarted),
                Conversions = g.Sum(x => x.Conversions),
                Revenue = g.Sum(x => x.Revenue),
            })
            .OrderByDescending(x => x.Revenue)
            .ThenByDescending(x => x.Conversions)
            .ToList();

        var response = new PromotionalCampaignPerformanceResponseDto
        {
            GeneratedAtUtc = DateTime.UtcNow,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            TotalLinks = links.Count,
            Visits = byLink.Sum(x => x.Visits),
            EmailCaptured = byLink.Sum(x => x.EmailCaptured),
            Signups = byLink.Sum(x => x.Signups),
            CheckoutStarted = byLink.Sum(x => x.CheckoutStarted),
            Conversions = byLink.Sum(x => x.Conversions),
            Revenue = byLink.Sum(x => x.Revenue),
            ByLink = byLink,
            ByRep = byRep,
        };

        return Ok(response);
    }

    private async Task<string> GenerateUniquePublicCodeAsync()
    {
        while (true)
        {
            var candidate = Convert
                .ToHexString(RandomNumberGenerator.GetBytes(8))
                .ToUpperInvariant();

            var exists = await _context.PromotionalCampaignLinks
                .AnyAsync(x => x.PublicCode == candidate);
            if (!exists)
            {
                return candidate;
            }
        }
    }

    private sealed class PerformanceEventRow
    {
        public int PromotionalCampaignLinkId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public decimal? RevenueAmount { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}

