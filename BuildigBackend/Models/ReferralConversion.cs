namespace BuildigBackend.Models;

public class ReferralConversion
{
    public long Id { get; set; }
    public int ReferralLinkId { get; set; }
    public string ReferrerUserId { get; set; } = string.Empty;
    public string ReferredUserId { get; set; } = string.Empty;
    public string? StripeSubscriptionId { get; set; }
    public int FriendDiscountPercent { get; set; } = 15;
    public int ReferrerCreditPercent { get; set; } = 15;
    public bool ReferrerCreditApplied { get; set; } = false;
    public DateTime ConvertedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReferrerCreditAppliedAtUtc { get; set; }
}
