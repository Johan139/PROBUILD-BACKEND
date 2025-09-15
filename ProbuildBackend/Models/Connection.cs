using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class Connection
    {
        [Key]
        public Guid Id { get; set; }
        public string RequesterId { get; set; }
        public string ReceiverId { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}