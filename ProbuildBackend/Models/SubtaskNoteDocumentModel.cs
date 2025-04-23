namespace ProbuildBackend.Models
{
    public class SubtaskNoteDocumentModel
    {
        public int Id { get; set; }
        public int? NoteId { get; set; }
        public string FileName { get; set; }
        public string BlobUrl { get; set; }
        public string sessionId { get; set; }
        public DateTime UploadedAt { get; set; }
    }
}
