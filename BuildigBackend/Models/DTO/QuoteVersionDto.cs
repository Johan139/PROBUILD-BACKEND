namespace BuildigBackend.Models.DTO
{
    public class QuoteVersionDto
    {
        public int Version { get; set; }

        public string Header { get; set; }   // QUOTE | INVOICE

        public string From { get; set; }
        public string To { get; set; }
        public string ClientAddress { get; set; }
        public string ClientPhone { get; set; }
        public string ClientEmail { get; set; }

        public string ProjectName { get; set; }
        public string ProjectAddress { get; set; }
        public string? PaymentTerms { get; set; }
        public DateTime? Date { get; set; }
        public DateTime? DueDate { get; set; }
        public Guid? LogoId { get; set; }
        public string Notes { get; set; }
        public string Terms { get; set; }

        public decimal Total { get; set; }
    }

}

