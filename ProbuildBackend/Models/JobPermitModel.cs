using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class JobPermitModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int JobId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; }

        [MaxLength(255)]
        public string? IssuingAgency { get; set; }

        public string? Requirements { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending";

        public DateTime? StartDate { get; set; }

        public DateTime? ExpirationDate { get; set; }

        public int? DocumentId { get; set; }

        [ForeignKey("DocumentId")]
        public virtual JobDocumentModel? Document { get; set; }

        [Required]
        public bool IsAiGenerated { get; set; } = false;
    }
}
