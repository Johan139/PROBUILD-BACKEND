using Microsoft.AspNetCore.Identity.UI.Services;
using SendGrid;
using SendGrid.Helpers.Mail;

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
