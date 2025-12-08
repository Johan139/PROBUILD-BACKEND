using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class QuoteExtraCost
    {
        [Key]
        public int Id { get; set; }
        public string QuoteId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public string Title { get; set; } = string.Empty;
        public Quote Quote { get; set; } = null!;
    }
}
