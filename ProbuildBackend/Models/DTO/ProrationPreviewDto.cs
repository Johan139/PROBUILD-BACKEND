namespace ProbuildBackend.Models.DTO
{
    public class ProrationPreviewDto
    {
        public long ProrationDateUnix { get; set; }
        public string Currency { get; set; }
        public decimal ProrationSubtotal { get; set; }
        public decimal PreviewTotal { get; set; }
        public DateTime? NextBillingDate { get; set; }
        public List<ProrationPreviewLineDto> ProrationLines { get; set; }
    }
}
