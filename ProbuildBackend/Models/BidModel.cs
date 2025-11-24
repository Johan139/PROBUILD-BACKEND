using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ProbuildBackend.Models
{
    public class BidModel
    {
        [Key]
        public int Id { get; set; }
        public string? Task { get; set; }
        public int Duration { get; set; }
        public int JobId { get; set; }

        // Prevent circular loops
        [JsonIgnore]
        public JobModel? Job { get; set; }

        public string? UserId { get; set; }

        // Prevent circular loops
        [JsonIgnore]
        public UserModel? User { get; set; }

        public decimal Amount { get; set; }
        public int BiddingRound { get; set; }
        public bool IsFinalist { get; set; }
        public string Status { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string? DocumentUrl { get; set; }
        public string? QuoteId { get; set; }

        // Prevent circular loops
        [JsonIgnore]
        public Quote? Quote { get; set; }
    }
}