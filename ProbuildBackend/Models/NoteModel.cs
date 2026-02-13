using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class NoteModel
    {
        [Key]
        public int Id { get; set; }

        public int? JobId { get; set; }

        public int? TradePackageId { get; set; }

        [Required]
        [MaxLength(2000)]
        public string NoteText { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Visibility { get; set; } = "private";

        [Required]
        [MaxLength(450)]
        public string CreatedByUserId { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
