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

        [ForeignKey("ProjectId")]
        public int ProjectId { get; set; }
        public ProjectModel? Project { get; set; }

        [ForeignKey("UserId")]
        public string UserId { get; set; }
        public UserModel? User { get; set; }

        // Broadcast group or specific recipients
        public List<string> Recipients { get; set; } // You can define Recipients as a list for flexibility.
    }
}
