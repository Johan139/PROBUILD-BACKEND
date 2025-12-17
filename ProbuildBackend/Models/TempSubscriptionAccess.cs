using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class TempSubscriptionAccess
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }

        [Required]
        public string JobId { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }

        public string OriginalSubscriptionTier { get; set; }

        [Required]
        public bool IsActive { get; set; }

        [ForeignKey("UserId")]
        public virtual UserModel User { get; set; }
    }
}
