using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    [Table("vw_Notifications")]
    public class NotificationView
    {
        public int Id { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public int JobId { get; set; }
        public string ProjectName { get; set; }
        public string RecipientId { get; set; }
        public string RecipientFirstName { get; set; }
        public string RecipientLastName { get; set; }
        public string SenderId { get; set; }
        public string SenderFirstName { get; set; }
        public string SenderLastName { get; set; }
        public bool? IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
    }
}
