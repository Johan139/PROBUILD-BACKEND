namespace ProbuildBackend.Models
{
    public class QuoteVersionModel
    {
        public Guid Id { get; set; }

        public Guid QuoteId { get; set; }
        public Quote Quote { get; set; }

        public int Version { get; set; }

        public string Header { get; set; }
        public string ClientAddress { get; set; }
        public string ClientPhone { get; set; }
        public string ClientEmail { get; set; }
        public LogosModel? Logo { get; set; }
        public Guid? LogoId { get; set; }
        public string ProjectName { get; set; }
        public string ProjectAddress { get; set; }
        public string From { get; set; }
        public string To { get; set; }

        public DateTime? Date { get; set; }
        public DateTime? DueDate { get; set; }

        public string? Notes { get; set; }
        public string? Terms { get; set; }

        public decimal Total { get; set; }

        public DateTime CreatedDate { get; set; }

        public ICollection<QuoteRow> Rows { get; set; } = new List<QuoteRow>();
        public ICollection<QuoteExtraCost> ExtraCosts { get; set; } = new List<QuoteExtraCost>();
    }
}
