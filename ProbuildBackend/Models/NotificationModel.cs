using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class NotificationModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Message { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [ForeignKey("JobId")]
        public int JobId { get; set; }
        public JobModel? Job { get; set; }

        [ForeignKey("UserId")]
        public string UserId { get; set; } // The user receiving the notification
        public UserModel? User { get; set; }

        [Required]
        [ForeignKey("Sender")]
        public string SenderId { get; set; } // The user who sent the notification
        public UserModel? Sender { get; set; }

        public List<string> Recipients { get; set; }
    }
}
