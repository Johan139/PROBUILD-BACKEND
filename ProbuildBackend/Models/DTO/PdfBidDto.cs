namespace ProbuildBackend.Models.DTO
{
    public class PdfBidDto
    {
        public int JobId { get; set; }
        public int? TradePackageId { get; set; }
        public string DocumentUrl { get; set; }
    }
}
