namespace BuildigBackend.Models.DTO
{
    public class SubscriptionPaymentRequestDTO
    {
        public string UserId { get; set; }
        public string PackageName { get; set; }
        public decimal Amount { get; set; }
        public string Source { get; set; } // <-- add this
        public string AssignedUser { get; set; }

        public string BillingCycle { get; set; }

        public string SubscriptionId { get; set; }

        /// <summary>Optional expo Gold Card / Coupon code captured from URL or session.</summary>
        public string? PromotionalLinkCode { get; set; }
        /// <summary>Optional referral code captured from URL (?ref=).</summary>
        public string? ReferralCode { get; set; }
    }
}

