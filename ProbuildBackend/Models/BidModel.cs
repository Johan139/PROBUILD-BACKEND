using System.ComponentModel.DataAnnotations;

// Ensure Bit is a class, not a struct
namespace ProbuildBackend.Models
{
    public class BidModel
    {
        [Key]
        public int Id { get; set; }
        public string? Task { get; set; }
        public int Duration { get; set; }
        public int JobId { get; set; }
        public JobModel? Job { get; set; }
        public string? UserId { get; set; }
        public UserModel? User { get; set; }
        public decimal Amount { get; set; }
        public int BiddingRound { get; set; }
        public bool IsFinalist { get; set; }
        public string Status { get; set; }
        public byte[]? Quote { get; set; }
        public DateTime SubmittedAt { get; set; }
    }
}