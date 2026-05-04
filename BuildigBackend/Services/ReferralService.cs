using System.Security.Cryptography;
using BuildigBackend.Models;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace BuildigBackend.Services;

public class ReferralService
{
    private readonly ApplicationDbContext _context;

    public ReferralService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ReferralLink> GetOrCreateLinkForUserAsync(string referrerUserId)
    {
        var normalizedUserId = (referrerUserId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedUserId))
            throw new ArgumentException("Referrer user id is required.", nameof(referrerUserId));

        var existing = await _context.ReferralLinks.FirstOrDefaultAsync(x =>
            x.ReferrerUserId == normalizedUserId && x.IsActive
        );
        if (existing != null)
            return existing;

        var code = await GenerateUniqueCodeAsync();
        var created = new ReferralLink
        {
            ReferrerUserId = normalizedUserId,
            Code = code,
            IsActive = true,
        };
        _context.ReferralLinks.Add(created);
        await _context.SaveChangesAsync();
        return created;
    }

    public async Task<ReferralLink?> ResolveActiveLinkAsync(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var normalized = code.Trim().ToUpperInvariant();
        return await _context.ReferralLinks.FirstOrDefaultAsync(x => x.Code == normalized && x.IsActive);
    }

    public async Task<string> EnsureReferralCouponAsync(ReferralLink link, int discountPercent = 15)
    {
        if (!string.IsNullOrWhiteSpace(link.StripeCouponId))
            return link.StripeCouponId!;

        var couponService = new CouponService();
        var created = await couponService.CreateAsync(
            new CouponCreateOptions
            {
                PercentOff = discountPercent,
                Duration = "once",
                Name = $"Referral {link.Code}",
                Metadata = new Dictionary<string, string>
                {
                    { "referralLinkId", link.Id.ToString() },
                    { "referralCode", link.Code },
                },
            }
        );
        link.StripeCouponId = created.Id;
        await _context.SaveChangesAsync();
        return created.Id;
    }

    public async Task RecordConversionAsync(
        int referralLinkId,
        string referrerUserId,
        string referredUserId,
        string? stripeSubscriptionId
    )
    {
        var referrer = (referrerUserId ?? string.Empty).Trim();
        var referred = (referredUserId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(referrer) || string.IsNullOrWhiteSpace(referred))
            return;
        if (string.Equals(referrer, referred, StringComparison.OrdinalIgnoreCase))
            return;

        var exists = await _context.ReferralConversions.AnyAsync(x =>
            x.ReferralLinkId == referralLinkId
            && x.ReferredUserId == referred
            && (string.IsNullOrWhiteSpace(stripeSubscriptionId) || x.StripeSubscriptionId == stripeSubscriptionId)
        );
        if (exists)
            return;

        _context.ReferralConversions.Add(
            new ReferralConversion
            {
                ReferralLinkId = referralLinkId,
                ReferrerUserId = referrer,
                ReferredUserId = referred,
                StripeSubscriptionId = stripeSubscriptionId,
                FriendDiscountPercent = 15,
                ReferrerCreditPercent = 15,
                ConvertedAtUtc = DateTime.UtcNow,
            }
        );
        await _context.SaveChangesAsync();
    }

    private async Task<string> GenerateUniqueCodeAsync()
    {
        while (true)
        {
            var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToUpperInvariant();
            var code = $"REF-{raw}";
            var exists = await _context.ReferralLinks.AnyAsync(x => x.Code == code);
            if (!exists)
                return code;
        }
    }
}
