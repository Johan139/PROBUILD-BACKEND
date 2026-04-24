using BuildigBackend.Models;

namespace BuildigBackend.Interface
{
    public interface IEmailTemplateService
    {
        Task<EmailTemplate> GetTemplateAsync(string templateName);
    }
}

