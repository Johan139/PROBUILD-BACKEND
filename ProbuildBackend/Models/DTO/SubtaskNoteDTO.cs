namespace ProbuildBackend.Models.DTO
{
    public class SubtaskNoteDTO
    {
        public int JobId { get; set; }

        public int JobSubtaskId { get; set; }

        public string NoteText { get; set; }

        public string CreatedByUserId { get; set; } // assuming user IDs are stored as strings (e.g., GUIDs)

        public DateTime CreatedAt { get; set; }

        public DateTime? ModifiedAt { get; set; }

        public string? SessionId { get; set; } // Add sessionId to link documents

        public List<string> UserIds { get; set; }
    }
}
