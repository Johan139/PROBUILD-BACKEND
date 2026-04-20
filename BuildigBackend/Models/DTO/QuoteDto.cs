namespace BuildigBackend.Models.DTO
{
    public class QuoteDto
    {
        public Guid? QuoteId { get; set; }
        public int? JobID { get; set; }
        public string Number { get; set; }
        public string DocumentType { get; set; } // QUOTE | INVOICE

        public string From { get; set; }
        public string To { get; set; }
        public string? Notes { get; set; }
        public string? Terms { get; set; }
        public DateTime? Date { get; set; }
        public DateTime? DueDate { get; set; }
        public string ClientAddress { get; set; }
        public string ClientPhone { get; set; }
        public string ClientEmail { get; set; }
        public string? PaymentTerms { get; set; }
        public string ProjectName { get; set; }
        public string ProjectAddress { get; set; }
        public decimal Total { get; set; }

        public string CreatedID { get; set; }
        public string CreatedBy { get; set; }
        public Guid? LogoId { get; set; }
        public List<QuoteRowDto> Rows { get; set; } = [];
        public List<QuoteExtraCostDto> ExtraCosts { get; set; } = [];
    }
}

