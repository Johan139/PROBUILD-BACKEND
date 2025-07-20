namespace ProbuildBackend.Models
{
    public class NotifyTimelineUpdateRequest
    {
        public int JobId { get; set; }
        public int SubtaskId { get; set; }
        public string SenderId { get; set; }
    }
}
