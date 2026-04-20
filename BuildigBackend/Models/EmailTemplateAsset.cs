using System.ComponentModel.DataAnnotations;

namespace BuildigBackend.Models
{
    public class EmailTemplateAsset
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Kind { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Url { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}

