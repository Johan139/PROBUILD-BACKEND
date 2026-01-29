using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class Quote
    {
        public Guid Id { get; set; }

        public int? JobID { get; set; }

        public string Number { get; set; }

        public string DocumentType { get; set; } 

        public string Status { get; set; }
        public int CurrentVersion { get; set; }
        public ICollection<QuoteVersionModel> Versions { get; set; } = new List<QuoteVersionModel>();
        public Guid? LogoId { get; set; }
        public LogosModel Logo { get; set; }
        public string CreatedID { get; set; }
        public string CreatedBy { get; set; }
        public string? SentTo { get; set; }
        public DateTime CreatedDate { get; set; }
    }

}
