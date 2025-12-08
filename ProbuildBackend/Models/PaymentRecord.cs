namespace ProbuildBackend.Models
{
    public class PaymentRecord
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public string Package { get; set; }
        public string StripeSessionId { get; set; }
        public string Status { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaidAt { get; set; }
        public DateTime ValidUntil { get; set; }

        public bool? IsTrial { get; set; }
        public string? SubscriptionID { get; set; }
        public bool? Cancelled { get; set; }

        public DateTime? CancelledDate { get; set; }

        public string? AssignedUser { get; set; }
    }
}
