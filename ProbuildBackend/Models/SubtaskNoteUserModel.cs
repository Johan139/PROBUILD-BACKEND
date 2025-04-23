namespace ProbuildBackend.Models
{
    public class SubtaskNoteUserModel
    {
        public int Id { get; set; }
        public int SubtaskNoteId { get; set; }

        public string UserId { get; set; }
    }
}
