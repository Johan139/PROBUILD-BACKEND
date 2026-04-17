using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class UploadDocumentDTO
    {
        [Required]
        public List<IFormFile> Blueprint { get; set; }
        public int? JobId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; }
        public string? connectionId { get; set; }
        public string? sessionId { get; set; }
        public string? Category { get; set; }
        public string? SubType { get; set; }
        public string? DocumentName { get; set; }
        public string? Issuer { get; set; }
        public string? Number { get; set; }
        public DateTime? IssueDate { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public string? CoverageAmount { get; set; }
        public string? AggregateLimit { get; set; }
    }
}