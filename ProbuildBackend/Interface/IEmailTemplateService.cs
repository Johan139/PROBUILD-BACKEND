using ProbuildBackend.Models;

namespace ProbuildBackend.Interface
{
    public interface IEmailTemplateService
    {
        Task<EmailTemplate> GetTemplateAsync(string templateName);
    }
}
