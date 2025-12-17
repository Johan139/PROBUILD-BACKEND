namespace ProbuildBackend.Models
{
    public class ProfileDocuments
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public string FileName { get; set; }
        public string BlobUrl { get; set; }
        public string sessionId { get; set; }
        public DateTime UploadedAt { get; set; }

        public DateTime? DeletedAt { get; set; }
        public bool? Deleted { get; set; }
    }
}
