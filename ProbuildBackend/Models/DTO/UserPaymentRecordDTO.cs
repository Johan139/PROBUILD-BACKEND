namespace ProbuildBackend.Models.DTO
{
    public class UserPaymentRecordDTO
    {
        public string userId { get; set; }
        public string Package { get; set; }
        public DateTime ValidUntil { get; set; }
        public decimal Amount { get; set; }
        public string AssignedUser { get; set; }
        public string AssignedUserName { get; set; }
        public string Status { get; set; }
        public string SubscriptionID { get; set; }
    }
}
