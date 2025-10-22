using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class Invoice
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public int JobId { get; set; }

        [Required]
        public string UploaderId { get; set; }

        [Required]
        public string FilePath { get; set; }

        [Required]
        public string Status { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required]
        public DateTime UploadedAt { get; set; }
    }
}