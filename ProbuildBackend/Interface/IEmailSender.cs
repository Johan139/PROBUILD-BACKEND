using ProbuildBackend.Models;

namespace ProbuildBackend.Interface
{
    public interface IEmailSender
    {
        Task SendEmailAsync(EmailTemplate emailTemplate, string email);
        Task SendEmailWithAttachmentAsync(
    EmailTemplate template,
    string toEmail,
    byte[] attachmentBytes,
    string attachmentFileName,
    string attachmentContentType = "application/pdf"
);
    }
}
