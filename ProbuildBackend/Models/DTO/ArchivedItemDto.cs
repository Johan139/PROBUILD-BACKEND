namespace ProbuildBackend.Models.DTO
{
    public class ArchivedItemDto
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public string Status { get; set; }
        public DateTime? ArchivedAt { get; set; }
        // Quotes / invoices
        public string Client { get; set; }
        public decimal? Amount { get; set; }
        // Documents
        public string Project { get; set; }
        public string DocumentType { get; set; }
        public long? Size { get; set; }
        // Trade Packages (Job Postings)
        public string TradeName { get; set; }
        public int? BidsCount { get; set; }
        public string JobId { get; set; }
    }


}
