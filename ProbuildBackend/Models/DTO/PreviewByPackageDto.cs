namespace ProbuildBackend.Models.DTO
{
    public class PreviewByPackageDto
    {
        public int PackageId { get; set; }
        public string CustomerId { get; set; }
        public string SubscriptionId { get; set; }
        public string ExistingSubscriptionItemId { get; set; }
    }
}
