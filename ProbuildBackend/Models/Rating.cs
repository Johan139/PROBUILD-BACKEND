using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class Rating
    {
        [Key]
        public Guid Id { get; set; }
        public int JobId { get; set; }
        public string ReviewerId { get; set; }
        public string RatedUserId { get; set; }
        public int RatingValue { get; set; }
        public string ReviewText { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
