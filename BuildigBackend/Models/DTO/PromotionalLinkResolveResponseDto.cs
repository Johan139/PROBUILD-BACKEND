namespace BuildigBackend.Models.DTO;

public class PromotionalLinkResolveResponseDto
{
    public bool Valid { get; set; }

    /// <summary>GoldCard | ExpoCoupon when valid.</summary>
    public string? CampaignKind { get; set; }

    public int Month1DiscountPercent { get; set; }

    public int? Month2DiscountPercent { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public string? RepLabel { get; set; }

    public string? AllowedPackageName { get; set; }

    public string? AllowedBillingCycle { get; set; }

    public string? Message { get; set; }
}
