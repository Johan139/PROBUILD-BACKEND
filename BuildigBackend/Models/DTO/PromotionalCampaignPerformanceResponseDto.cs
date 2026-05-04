namespace BuildigBackend.Models.DTO;

public class PromotionalCampaignPerformanceResponseDto
{
    public DateTime GeneratedAtUtc { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int TotalLinks { get; set; }
    public int Visits { get; set; }
    public int EmailCaptured { get; set; }
    public int Signups { get; set; }
    public int CheckoutStarted { get; set; }
    public int Conversions { get; set; }
    public decimal Revenue { get; set; }
    public List<PromotionalCampaignPerformanceByLinkDto> ByLink { get; set; } = new();
    public List<PromotionalCampaignPerformanceByRepDto> ByRep { get; set; } = new();
}

public class PromotionalCampaignPerformanceByLinkDto
{
    public int PromotionalCampaignLinkId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? PublicCode { get; set; }
    public string CampaignKind { get; set; } = string.Empty;
    public string? RepLabel { get; set; }
    public int Visits { get; set; }
    public int EmailCaptured { get; set; }
    public int Signups { get; set; }
    public int CheckoutStarted { get; set; }
    public int Conversions { get; set; }
    public decimal Revenue { get; set; }
    public int CouponConvertedDay1To3 { get; set; }
    public int CouponConvertedDay4To7 { get; set; }
    public int CouponConvertedDay8To14 { get; set; }
}

public class PromotionalCampaignPerformanceByRepDto
{
    public string RepLabel { get; set; } = string.Empty;
    public int LinkCount { get; set; }
    public int Visits { get; set; }
    public int EmailCaptured { get; set; }
    public int Signups { get; set; }
    public int CheckoutStarted { get; set; }
    public int Conversions { get; set; }
    public decimal Revenue { get; set; }
}
