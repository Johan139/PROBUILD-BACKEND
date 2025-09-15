namespace ProbuildBackend.Models.DTO
{
    public class RatingDto
    {
        public int JobId { get; set; }
        public string RatedUserId { get; set; }
        public int RatingValue { get; set; }
        public string ReviewText { get; set; }
    }
}