using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class QuoteRow
    {
        [Key]
        public int Id { get; set; }
        public string QuoteId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Total { get; set; }
    }
}