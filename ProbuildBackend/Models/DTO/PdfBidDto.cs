namespace ProbuildBackend.Models.DTO
{
    public class PdfBidDto
    {
        public int JobId { get; set; }
        public int? TradePackageId { get; set; }
        public string DocumentUrl { get; set; }
        public decimal? Amount { get; set; }
        public string? Inclusions { get; set; }
        public string? Exclusions { get; set; }
        public Guid? QuoteId { get; set; }
    }
}
