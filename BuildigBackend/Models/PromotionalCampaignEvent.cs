namespace BuildigBackend.Models;

/// <summary>
/// Event stream per promotional access point for funnel analytics.
/// </summary>
public class PromotionalCampaignEvent
{
    public long Id { get; set; }
    public int PromotionalCampaignLinkId { get; set; }
    public PromotionalCampaignLink PromotionalCampaignLink { get; set; } = null!;

    /// <summary>
    /// visit | email_captured | signup_completed | checkout_started | converted
    /// </summary>
    public string EventType { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? UserId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public decimal? RevenueAmount { get; set; }
    public string? Currency { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

