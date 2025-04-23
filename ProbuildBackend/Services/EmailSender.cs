using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Threading.Tasks;

namespace ProbuildBackend.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;

        public EmailSender(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var apiKey = Environment.GetEnvironmentVariable("SENDGRID_KEY") ?? _configuration["SendGrid:ApiKey"];
            var sendgridEmail = Environment.GetEnvironmentVariable("SENDGRID_EMAIL") ?? _configuration["SendGrid:Email"];
            var toEmail = email;
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentNullException(nameof(apiKey), "SendGrid API Key is not configured.");
            }
            var client = new SendGridClient(apiKey);
            var from = new EmailAddress(sendgridEmail, "ProBuild");
            var to = new EmailAddress(toEmail);
            var msg = MailHelper.CreateSingleEmail(from, to, subject, "", htmlMessage);
           var test = await client.SendEmailAsync(msg);
        }
    }
}
