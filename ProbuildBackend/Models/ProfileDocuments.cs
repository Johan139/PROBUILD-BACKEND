namespace ProbuildBackend.Models
{
    public class ProfileDocuments
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public string FileName { get; set; }
        public string BlobUrl { get; set; }
        public string sessionId { get; set; }
        public DateTime UploadedAt { get; set; }
        public string? Category { get; set; }
        public string? SubType { get; set; }
        public string? DocumentName { get; set; }
        public string? Issuer { get; set; }
        public string? Number { get; set; }
        public DateTime? IssueDate { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public string? CoverageAmount { get; set; }
        public string? AggregateLimit { get; set; }

        public DateTime? ArchivedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public bool? Deleted { get; set; }
    }
}
