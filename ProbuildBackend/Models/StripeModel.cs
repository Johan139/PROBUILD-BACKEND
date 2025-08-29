using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class StripeModel
    {
        [Key]
        public long Id { get; set; }
        public string Subscription { get; set; }
        public decimal Amount { get; set; }

        public string? StripeProductId { get; set; }

        public string? StripeProductIdAnually { get; set; }

        public decimal? AnnualAmount { get; set; }
    }
}
