namespace BuildigBackend.Models.DTO;

public class PromotionalCampaignEventAdminDto
{
    public long Id { get; set; }
    public int PromotionalCampaignLinkId { get; set; }
    public string PromotionalCode { get; set; } = string.Empty;
    public string? PromotionalPublicCode { get; set; }
    public string CampaignKind { get; set; } = string.Empty;
    public string? RepLabel { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? UserId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public decimal? RevenueAmount { get; set; }
    public string? Currency { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
