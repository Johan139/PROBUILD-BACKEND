namespace ProbuildBackend.Models.DTO
{
    public class SubscriptionUpgradeDTO
    {
        public string subscriptionId { get; set; }
        public string packageName { get; set; }

        public string userId { get; set; }
        public string AssignedUser { get; set; }
    }
}
