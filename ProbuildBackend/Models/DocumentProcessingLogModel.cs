namespace ProbuildBackend.Models
{
    public class DocumentProcessingLogModel
    {
        public int Id { get; set; }
        public string Description { get; set; }
        public DateTime DateCreated { get; set; }
        public string Location { get; set; }
    }
}
