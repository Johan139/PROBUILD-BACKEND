namespace ProbuildBackend.Models.DTO
{
    public class QuoteExtraCostDto
    {
        public string Type { get; set; }
        // Examples: "Discount", "Vat", "Fee"

        public decimal Value { get; set; }

        public string Title { get; set; }
    }

}
