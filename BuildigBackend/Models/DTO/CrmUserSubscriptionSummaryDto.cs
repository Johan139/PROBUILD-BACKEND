namespace BuildigBackend.Models.DTO
{
    public class CrmUserSubscriptionSummaryDto
    {
        public bool HasActiveSubscription { get; set; }
        public string? Status { get; set; }
        public string? Package { get; set; }
        public System.DateTime? ValidUntil { get; set; }
        public decimal? Amount { get; set; }
        public bool? IsTrial { get; set; }
        public bool? Cancelled { get; set; }
        public System.DateTime? CancelledDate { get; set; }
        public string? SubscriptionId { get; set; }
    }
}

