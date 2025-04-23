using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class SubtaskNoteDocumentDTO
    {      
        [Required]
        public int NoteId { get; set; }
        public string FileName { get; set; }
        public string BlobUrl { get; set; }
        public string sessionId { get; set; }
        public DateTime UploadedAt { get; set; }
    }
}
