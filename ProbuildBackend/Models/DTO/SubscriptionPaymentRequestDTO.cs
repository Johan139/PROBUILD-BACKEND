namespace ProbuildBackend.Models.DTO
{
    public class SubscriptionPaymentRequestDTO
    {
        public string UserId { get; set; }
        public string PackageName { get; set; }
        public decimal Amount { get; set; }
        public string Source { get; set; }  // <-- add this
        public string AssignedUser { get; set; }
    }
}
