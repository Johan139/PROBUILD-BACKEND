namespace ProbuildBackend.Models.DTO
{
    public class UploadQuoteDto
    {
        public List<IFormFile> Quote { get; set; }
        public string sessionId { get; set; }
        public string connectionId { get; set; }
    }
}
