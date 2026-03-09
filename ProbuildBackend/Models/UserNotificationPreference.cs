using Elastic.Apm.Api;
using ProbuildBackend.Models.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class UserNotificationPreference
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string UserId { get; set; }

        [Required]
        public NotificationType NotificationType { get; set; }

        [Required]
        public NotificationChannel Channel { get; set; }

        [Required]
        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        public DateTime? ModifiedDate { get; set; }

        /* ================= NAVIGATION ================= */

        public User User { get; set; }
    }
}
