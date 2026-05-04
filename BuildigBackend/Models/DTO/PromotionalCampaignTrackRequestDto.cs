namespace BuildigBackend.Models.DTO;

public class PromotionalCampaignTrackRequestDto
{
    public string Code { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? UserId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public decimal? RevenueAmount { get; set; }
    public string? Currency { get; set; }
}

