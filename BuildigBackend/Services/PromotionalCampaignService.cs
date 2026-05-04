using BuildigBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BuildigBackend.Services;

public class PromotionalCampaignService
{
    private readonly ApplicationDbContext _context;

    public PromotionalCampaignService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Returns an active link or null if missing, expired, inactive, or redemption-capped.
    /// </summary>
    public async Task<PromotionalCampaignLink?> TryResolveActiveLinkAsync(
        string? codeOrPublicCode,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(codeOrPublicCode))
            return null;

        var normalized = codeOrPublicCode.Trim().ToUpperInvariant();

        var link = await _context
            .PromotionalCampaignLinks.AsNoTracking()
            .FirstOrDefaultAsync(
                l => (l.Code == normalized || l.PublicCode == normalized) && l.IsActive,
                cancellationToken
            );

        if (link == null)
            return null;

        if (link.ExpiresAtUtc.HasValue && link.ExpiresAtUtc.Value < DateTime.UtcNow)
            return null;

        if (
            link.MaxRedemptions.HasValue
            && link.CurrentRedemptionCount >= link.MaxRedemptions.Value
        )
            return null;

        return link;
    }

    public async Task<PromotionalCampaignEvent?> TrackEventByCodeAsync(
        string? code,
        string eventType,
        string? email = null,
        string? userId = null,
        string? stripeSubscriptionId = null,
        decimal? revenueAmount = null,
        string? currency = null,
        CancellationToken cancellationToken = default
    )
    {
        var link = await TryResolveActiveLinkAsync(code, cancellationToken);
        if (link == null)
            return null;

        var evt = new PromotionalCampaignEvent
        {
            PromotionalCampaignLinkId = link.Id,
            EventType = eventType.Trim().ToLowerInvariant(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim(),
            StripeSubscriptionId = string.IsNullOrWhiteSpace(stripeSubscriptionId)
                ? null
                : stripeSubscriptionId.Trim(),
            RevenueAmount = revenueAmount,
            Currency = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToLowerInvariant(),
            CreatedAtUtc = DateTime.UtcNow,
        };

        _context.PromotionalCampaignEvents.Add(evt);
        await _context.SaveChangesAsync(cancellationToken);
        return evt;
    }
}
