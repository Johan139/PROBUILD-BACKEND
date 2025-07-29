using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class UploadDocumentDTO
    {
        [Required]
        public List<IFormFile> Blueprint { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string connectionId { get; set; }
        public string sessionId { get; set; }

    }
}
