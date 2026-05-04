namespace BuildigBackend.Models.DTO;

public class PromotionalCampaignLinkAdminDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? PublicCode { get; set; }
    public PromotionalCampaignKind CampaignKind { get; set; }

    public string? RepLabel { get; set; }
    public string? RepUserId { get; set; }

    public int Month1DiscountPercent { get; set; }
    public int? Month2DiscountPercent { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public bool IsActive { get; set; }

    public int? MaxRedemptions { get; set; }
    public int CurrentRedemptionCount { get; set; }

    public string? StripeCouponId { get; set; }

    public string? AllowedPackageName { get; set; }
    public string? AllowedBillingCycle { get; set; }
}

