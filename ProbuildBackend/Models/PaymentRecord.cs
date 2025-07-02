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
        public DateTime ValidUntil { get; set; }  // ⬅️ New column

        public bool IsTrial { get; set; }
    }
}
