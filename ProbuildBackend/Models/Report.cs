namespace ProbuildBackend.Models
{
    public class Report
    {
        public Guid Id { get; set; }
        public string ReporterUserId { get; set; }
        public string ReportedUserId { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}