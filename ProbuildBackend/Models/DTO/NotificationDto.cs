using System.Collections.Generic;

namespace ProbuildBackend.Dtos

{
    public class NotificationDto
    {
        public string? Message { get; set; }
        public int ProjectId { get; set; }
        public string? UserId { get; set; }
        public List<string>? Recipients { get; set; }
    }
}
