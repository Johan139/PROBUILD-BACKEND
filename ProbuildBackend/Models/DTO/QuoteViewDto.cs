namespace ProbuildBackend.Models.DTO
{
    public class QuoteViewDto
    {
        public Guid QuoteId { get; set; }
        public string Number { get; set; }
        public string Status { get; set; }
        public string DocumentType { get; set; }

        public int CurrentVersion { get; set; }
        public string? LogoUrl { get; set; }
        public QuoteVersionDto Version { get; set; }

        public List<QuoteRowDto> Rows { get; set; } = new();
        public List<QuoteExtraCostDto> ExtraCosts { get; set; } = new();
    }

}
