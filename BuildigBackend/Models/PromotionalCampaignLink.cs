namespace BuildigBackend.Models;

/// <summary>
/// Tracks unique expo Gold Card / Coupon URLs for attribution and Stripe metadata.
/// Percent fields mirror commercial terms; billing automation may use StripeCouponId or webhooks.
/// </summary>
public class PromotionalCampaignLink
{
    public int Id { get; set; }

    /// <summary>Normalized uppercase slug (e.g. AUS-001).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Public random token used in URLs so internal codes are not exposed.
    /// </summary>
    public string? PublicCode { get; set; }

    public PromotionalCampaignKind CampaignKind { get; set; }

    /// <summary>Optional CRM / rep label for dashboards.</summary>
    public string? RepLabel { get; set; }

    /// <summary>
    /// Optional rep user id for stable attribution (linked to your CRM users table).
    /// </summary>
    public string? RepUserId { get; set; }

    public int Month1DiscountPercent { get; set; }

    /// <summary>Null when the campaign has no second-month discount (e.g. expo coupon).</summary>
    public int? Month2DiscountPercent { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>When set, redemption is blocked at or above this count.</summary>
    public int? MaxRedemptions { get; set; }

    public int CurrentRedemptionCount { get; set; }

    /// <summary>Optional Stripe Coupon ID for Checkout Session discounts (single-coupon flows).</summary>
    public string? StripeCouponId { get; set; }

    /// <summary>
    /// Optional package hard lock. When set, checkout must match this package name.
    /// </summary>
    public string? AllowedPackageName { get; set; }

    /// <summary>
    /// Optional billing-cycle hard lock: monthly | yearly.
    /// </summary>
    public string? AllowedBillingCycle { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum PromotionalCampaignKind : byte
{
    GoldCard = 1,
    ExpoCoupon = 2,
}
