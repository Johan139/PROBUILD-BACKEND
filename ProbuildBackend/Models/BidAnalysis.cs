using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class BidAnalysis
    {
        [Key]
        public int Id { get; set; }
        public int JobId { get; set; }
        public int BidId { get; set; }
        public string AnalysisResult { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}