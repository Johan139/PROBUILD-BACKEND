namespace ProbuildBackend.Models
{
    public class PaymentRecordHistoryModel
    {
        public int PaymentRecordHistoryId { get; set; }
        public int PaymentRecordId { get; set; }
        public string Status { get; set; }
        public DateTime PaidAt { get; set; }
        public DateTime ValidUntil { get; set; }
        public string StripeSessionId { get; set; }
        public decimal Amount { get; set; }
        public string PackageName { get; set; }

    }
}
