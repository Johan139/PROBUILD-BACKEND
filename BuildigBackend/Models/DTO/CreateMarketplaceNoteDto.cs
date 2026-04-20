using System.ComponentModel.DataAnnotations;

namespace BuildigBackend.Models.DTO
{
    public class CreateMarketplaceNoteDto
    {
        public int? JobId { get; set; }

        public int? TradePackageId { get; set; }

        [Required]
        [MaxLength(450)]
        public string CreatedByUserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(2000)]
        public string NoteText { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Visibility { get; set; } = "private";
    }
}

