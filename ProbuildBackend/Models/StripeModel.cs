using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class StripeModel
    {
        [Key]
        public long Id { get; set; }
        public string Subscription { get; set; }
        public decimal Amount { get; set; }
    }
}
