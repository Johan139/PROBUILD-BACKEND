namespace ProbuildBackend.Models.DTO
{
    public class NotificationDto
    {
        public int Id { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public int JobId { get; set; }
        public string ProjectName { get; set; }
        public string SenderFullName { get; set; }
    }
}
