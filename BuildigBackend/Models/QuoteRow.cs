using System.ComponentModel.DataAnnotations;

namespace BuildigBackend.Models
{
    public class QuoteRow
    {
        public Guid Id { get; set; }

        public Guid QuoteVersionId { get; set; } 
        public QuoteVersionModel QuoteVersion { get; set; }

        public string Description { get; set; }

        public decimal Quantity { get; set; }

        public string Unit { get; set; } 

        public decimal UnitPrice { get; set; }

        public decimal Total { get; set; }
    }
}

