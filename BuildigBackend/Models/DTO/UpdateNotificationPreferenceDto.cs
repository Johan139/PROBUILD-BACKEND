using BuildigBackend.Models.Enums;

namespace BuildigBackend.Models.DTO
{
    public class UpdateNotificationPreferenceDto
    {
        public NotificationType NotificationType { get; set; }
        public NotificationChannel Channel { get; set; }
        public bool IsEnabled { get; set; }
    }
}

