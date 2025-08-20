namespace ProbuildBackend.Models
{
    public class SubscriptionModel
    {
        public string ClientName { get; set; }
        public string ContactName { get; set; }
        public string ContactEmail { get; set; }
        public string PlanType { get; set; }
        public string PlanPrice { get; set; }
        public string StartDate { get; set; }
        public string NextBillingDate { get; set; }
        public int Seats { get; set; }
        public string PlatformTier { get; set; }
        public string CustomTerms { get; set; }
        public string PromoCode { get; set; }
        public string Tax { get; set; }
        public string TotalCharged { get; set; }
        public string PaymentMethod { get; set; }
    }
}
