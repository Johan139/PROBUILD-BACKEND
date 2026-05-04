namespace BuildigBackend.Models;

public class ReferralLink
{
    public int Id { get; set; }
    public string ReferrerUserId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? StripeCouponId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
