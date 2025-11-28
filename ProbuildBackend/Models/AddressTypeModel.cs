using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class AddressTypeModel
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;   // e.g. "Billing Address"

        [MaxLength(250)]
        public string? Description { get; set; }           // optional: explain usage

        public bool IsDefault { get; set; } = false;       // seed default types
        public int DisplayOrder { get; set; } = 0;         // control order in UI
    }
}