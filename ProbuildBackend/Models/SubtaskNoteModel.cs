namespace ProbuildBackend.Models
{
    public class SubtaskNoteModel
    {
        public int Id { get; set; }

        public int JobId { get; set; }

        public int JobSubtaskId { get; set; }

        public string NoteText { get; set; }

        public string CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? ModifiedAt { get; set; } // Nullable for when the note hasn't been edited

        public bool Approved { get; set; }
        public bool Rejected { get; set; }
        public bool Archived { get; set; }
        public string Status =>
            Archived ? "Archived"
            : Approved ? "Approved"
            : Rejected ? "Rejected"
            : "Pending";
    }
}
