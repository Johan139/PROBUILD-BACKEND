namespace ProbuildBackend.Models
{
    public class DocumentProcessingResult
    {
        public int Id { get; set; }
        public int JobId { get; set; }
        public int DocumentId { get; set; }
        public string BomJson { get; set; }
        public string MaterialsEstimateJson { get; set; }
        public string FullResponse { get; set; } // Add this
        public DateTime CreatedAt { get; set; }

        public JobModel Job { get; set; }
        public JobDocumentModel Document { get; set; }
    }
}
