namespace ProbuildBackend.Models.DTO
{
    public class PaginatedNotificationResponse
    {
        public List<NotificationDto> Notifications { get; set; }
        public int TotalCount { get; set; }
    }
}
