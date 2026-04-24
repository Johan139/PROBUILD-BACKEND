using BuildigBackend.Models;
using SendGrid;
using SendGrid.Helpers.Mail;
using Microsoft.EntityFrameworkCore;
using IEmailSender = BuildigBackend.Interface.IEmailSender;

namespace BuildigBackend.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(
            IConfiguration configuration,
            ApplicationDbContext context,
            ILogger<EmailSender> logger)
        {
            _configuration = configuration;
            _context = context;
            _logger = logger;

        }

        public async Task SendEmailAsync(EmailTemplate emailTemplate, string email)
        {
            var apiKey =
                Environment.GetEnvironmentVariable("SENDGRID_KEY")
                ?? _configuration["SendGrid:ApiKey"];
            var sendgridEmail = emailTemplate.FromEmail;
            var toEmail = email;
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentNullException(
                    nameof(apiKey),
                    "SendGrid API Key is not configured."
                );
            }

            var emailLogId = Guid.NewGuid();
            var log = new EmailLog
            {
                Id = emailLogId,
                ToEmail = toEmail,
                FromEmail = sendgridEmail,
                Subject = emailTemplate.Subject,
                TemplateId = emailTemplate.TemplateId,
                TemplateName = emailTemplate.TemplateName,
                Provider = "sendgrid",
                CreatedAt = DateTime.UtcNow,
            };

            _context.EmailLogs.Add(log);
            await _context.SaveChangesAsync();

            var client = new SendGridClient(apiKey);
            var from = new EmailAddress(sendgridEmail, "BuildIG");
            var to = new EmailAddress(toEmail);
            var msg = MailHelper.CreateSingleEmail(
                from,
                to,
                emailTemplate.Subject,
                "",
                emailTemplate.Body
            );

            if (msg.Personalizations == null || msg.Personalizations.Count == 0)
            {
                msg.Personalizations = new List<Personalization> { new Personalization() };
            }
            msg.Personalizations[0].CustomArgs = new Dictionary<string, string>
            {
                { "emailLogId", emailLogId.ToString() },
                { "templateId", emailTemplate.TemplateId.ToString() },
            };

            var response = await client.SendEmailAsync(msg);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Body.ReadAsStringAsync();
                Console.WriteLine(
                    $"Failed to send email to {toEmail}. Status: {response.StatusCode}. Body: {responseBody}"
                );
                throw new Exception(
                    $"Failed to send email. Status code: {response.StatusCode}. Details: {responseBody}"
                );
            }

            var providerMessageId = response.Headers?.TryGetValues("X-Message-Id", out var values) == true
                ? values.FirstOrDefault()
                : null;

            var sentAt = DateTime.UtcNow;
            _context.EmailLogEvents.Add(new EmailLogEvent
            {
                EmailLogId = emailLogId,
                Email = toEmail,
                Type = "sent",
                Timestamp = sentAt,
                SgEventId = providerMessageId,
            });

            var existingLog = await _context.EmailLogs.FirstOrDefaultAsync(x => x.Id == emailLogId);
            if (existingLog != null)
            {
                existingLog.LastEventType = "sent";
                existingLog.LastEventAt = sentAt;
            }

            await _context.SaveChangesAsync();
        }
        public async Task SendEmailWithAttachmentAsync(
            EmailTemplate template,
            string toEmail,
            byte[] attachmentBytes,
            string attachmentFileName,
            string attachmentContentType = "application/pdf")
        {
            var apiKey =
    Environment.GetEnvironmentVariable("SENDGRID_KEY")
    ?? _configuration["SendGrid:ApiKey"];
            var sendgridEmail = template.FromEmail;
            var client = new SendGridClient(apiKey);
            // Example using SendGrid:
            var msg = new SendGridMessage();
            msg.SetFrom(new EmailAddress(sendgridEmail, "BuildIG"));
            msg.AddTo(new EmailAddress(toEmail));
            msg.SetSubject(template.Subject);
            msg.AddContent(MimeType.Html, template.Body);

            var emailLogId = Guid.NewGuid();
            var log = new EmailLog
            {
                Id = emailLogId,
                ToEmail = toEmail,
                FromEmail = sendgridEmail,
                Subject = template.Subject,
                TemplateId = template.TemplateId,
                TemplateName = template.TemplateName,
                Provider = "sendgrid",
                CreatedAt = DateTime.UtcNow,
            };
            _context.EmailLogs.Add(log);
            await _context.SaveChangesAsync();

            if (msg.Personalizations == null || msg.Personalizations.Count == 0)
            {
                msg.Personalizations = new List<Personalization> { new Personalization() };
            }
            msg.Personalizations[0].CustomArgs = new Dictionary<string, string>
            {
                { "emailLogId", emailLogId.ToString() },
                { "templateId", template.TemplateId.ToString() },
            };

            // Add the attachment
            var attachment = new SendGrid.Helpers.Mail.Attachment
            {
                Content = Convert.ToBase64String(attachmentBytes),
                Filename = attachmentFileName,
                Type = attachmentContentType,
                Disposition = "attachment"
            };
            msg.AddAttachment(attachment);

            var response = await client.SendEmailAsync(msg);

            if (response.StatusCode != System.Net.HttpStatusCode.OK &&
                response.StatusCode != System.Net.HttpStatusCode.Accepted)
            {
                throw new Exception($"Failed to send email: {response.StatusCode}");
            }

            var providerMessageId = response.Headers?.TryGetValues("X-Message-Id", out var values) == true
                ? values.FirstOrDefault()
                : null;

            var sentAt = DateTime.UtcNow;
            _context.EmailLogEvents.Add(new EmailLogEvent
            {
                EmailLogId = emailLogId,
                Email = toEmail,
                Type = "sent",
                Timestamp = sentAt,
                SgEventId = providerMessageId,
            });

            var existingLog = await _context.EmailLogs.FirstOrDefaultAsync(x => x.Id == emailLogId);
            if (existingLog != null)
            {
                existingLog.LastEventType = "sent";
                existingLog.LastEventAt = sentAt;
            }

            await _context.SaveChangesAsync();
        }
    }
}

