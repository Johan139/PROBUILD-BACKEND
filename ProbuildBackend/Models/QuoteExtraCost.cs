namespace ProbuildBackend.Models
{
    public class QuoteExtraCost
    {
        public Guid Id { get; set; }

        public Guid QuoteVersionId { get; set; }
        public QuoteVersionModel QuoteVersion { get; set; }

        public string Type { get; set; }

        public decimal Value { get; set; }

        public string Title { get; set; }
    }
}
