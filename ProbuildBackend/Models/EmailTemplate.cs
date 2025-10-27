using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class EmailTemplate
    {

        [Key]
        public int TemplateId { get; set; }

        public string? TemplateName { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public string? Description { get; set; }
        public string? FromName { get; set; }
        public string? FromEmail { get; set; }

        public bool IsHtml { get; set; }

        public string? HeaderHtml { get; set; }
        public string? FooterHtml { get; set; }
        public string? LogoUrl { get; set; }
        public string? InlineCss { get; set; }
        public string? LanguageCode { get; set; }
    
        public bool IsActive { get; set; }
        public int VersionNumber { get; set; }

        public Guid? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }

        public Guid? ModifiedBy { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}
