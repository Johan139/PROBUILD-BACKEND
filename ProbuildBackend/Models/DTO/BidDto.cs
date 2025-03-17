namespace ProbuildBackend.Models.DTO
{
    public class BidDto
    {
        public string? Task { get; set; }
        public IFormFile? Quote { get; set; }
        public int Duration { get; set; }
        public int JobId { get; set; }
        public string? UserId { get; set; }
    }
}
