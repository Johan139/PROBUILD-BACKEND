using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class LogosModel
    {
        [Key]
        public Guid Id { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public string UploadedBy { get; set; }
        public DateTime UploadedAt { get; set; }
        public string Type { get; set; }
    }
}
