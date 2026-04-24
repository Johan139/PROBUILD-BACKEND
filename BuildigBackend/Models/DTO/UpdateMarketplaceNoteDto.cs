using System.ComponentModel.DataAnnotations;

namespace BuildigBackend.Models.DTO
{
    public class UpdateMarketplaceNoteDto
    {
        [Required]
        [MaxLength(450)]
        public string RequesterUserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(2000)]
        public string NoteText { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Visibility { get; set; } = "private";
    }
}

