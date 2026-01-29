namespace ProbuildBackend.Models.DTO
{
    public class QuoteRowDto
    {
        public string Description { get; set; }

        public decimal Quantity { get; set; }

        public string Unit { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal Total { get; set; }
    }

}
