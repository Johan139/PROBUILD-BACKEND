using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class Quote
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Header { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string ToTitle { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string ShipToTitle { get; set; } = string.Empty;
        public string ShipTo { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string PaymentTerms { get; set; } = string.Empty;
        public string DueDate { get; set; } = string.Empty;
        public string PoNumber { get; set; } = string.Empty;
        public string ItemHeader { get; set; } = string.Empty;
        public string QuantityHeader { get; set; } = string.Empty;
        public string UnitCostHeader { get; set; } = string.Empty;
        public string AmountHeader { get; set; } = string.Empty;
        public decimal AmountPaid { get; set; }
        public decimal ExtraCostValue { get; set; }
        public decimal TaxValue { get; set; }
        public decimal DiscountValue { get; set; }
        public decimal FlatTotalValue { get; set; }
        public string NotesTitle { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string TermsTitle { get; set; } = string.Empty;
        public string Terms { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string CreatedID { get; set; } = string.Empty;
        public int? JobID { get; set; }
        public decimal? Version { get; set; }
        public string? Status { get; set; }
        public List<QuoteRow> Rows { get; set; } = new List<QuoteRow>();
        public List<QuoteExtraCost> ExtraCosts { get; set; } = new List<QuoteExtraCost>();
        public Guid? LogoId { get; set; }
        public LogosModel Logo { get; set; }
    }
}