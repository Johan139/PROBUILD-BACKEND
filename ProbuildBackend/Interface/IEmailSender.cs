using ProbuildBackend.Models;

namespace ProbuildBackend.Interface
{
    public interface IEmailSender
    {
        Task SendEmailAsync(EmailTemplate emailTemplate, string email);
    }
}
