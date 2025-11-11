using Microsoft.AspNetCore.Identity.UI.Services;
using ProbuildBackend.Models;
using SendGrid;
using SendGrid.Helpers.Mail;
using IEmailSender = ProbuildBackend.Interface.IEmailSender;
namespace ProbuildBackend.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;

        public EmailSender(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendEmailAsync(EmailTemplate emailTemplate, string email)
        {
            var apiKey = Environment.GetEnvironmentVariable("SENDGRID_KEY") ?? _configuration["SendGrid:ApiKey"];
            var sendgridEmail = emailTemplate.FromEmail;
            var toEmail = email;
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentNullException(nameof(apiKey), "SendGrid API Key is not configured.");
            }
            var client = new SendGridClient(apiKey);
            var from = new EmailAddress(sendgridEmail, "ProBuild");
            var to = new EmailAddress(toEmail);
            var msg = MailHelper.CreateSingleEmail(from, to, emailTemplate.Subject, "", emailTemplate.Body);
            var response = await client.SendEmailAsync(msg);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                Console.WriteLine($"Failed to send email to {toEmail}. Status: {response.StatusCode}. Body: {responseBody}");
                throw new Exception($"Failed to send email. Status code: {response.StatusCode}. Details: {responseBody}");
            }
        }
    }
}
