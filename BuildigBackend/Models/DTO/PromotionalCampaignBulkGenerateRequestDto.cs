namespace BuildigBackend.Models.DTO;

public class PromotionalCampaignBulkGenerateRequestDto
{
    public string Prefix { get; set; } = "PROMO";
    public int Count { get; set; } = 10;
    public int StartNumber { get; set; } = 1;
    public PromotionalCampaignKind CampaignKind { get; set; } = PromotionalCampaignKind.ExpoCoupon;
    public string? RepLabel { get; set; }
    public string? RepUserId { get; set; }
    public int Month1DiscountPercent { get; set; } = 20;
    public int? Month2DiscountPercent { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public int? MaxRedemptions { get; set; }
    public string? AllowedPackageName { get; set; }
    public string? AllowedBillingCycle { get; set; }
}
