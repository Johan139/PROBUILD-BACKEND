namespace ProbuildBackend.Models
{
    public class JobDocumentModel
    {
        public int Id { get; set; }
        public int? JobId { get; set; }
        public string FileName { get; set; }
        public string BlobUrl { get; set; }
        public string SessionId { get; set; }
        public DateTime UploadedAt { get; set; }

        public long Size { get; set; }
    }
}
