using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;

namespace ProbuildBackend.Services
{
    public class EmailAutomationManager
    {
        private readonly ApplicationDbContext _db;
        private readonly IEmailSender _emailService;
        private readonly ILogger<EmailAutomationManager> _logger;
        private readonly IConfiguration _configuration;

        public EmailAutomationManager(
            ApplicationDbContext db,
            IEmailSender emailService,
            ILogger<EmailAutomationManager> logger,
            IConfiguration configuration)
        {
            _db = db;
            _emailService = emailService;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task ExecuteAutomationAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger.LogWarning("ExecuteAutomationAsync called with empty userId");
                return;
            }

            var user = await _db.Users.FindAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found", userId);
                return;
            }

            var activeRules = await _db.EmailAutomationRules
                .Where(r => r.IsActive)
                .ToListAsync();

            if (!activeRules.Any())
            {
                _logger.LogDebug("No active automation rules");
                return;
            }

            foreach (var rule in activeRules)
            {
                try
                {
                    await ProcessRuleAsync(user, rule);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process rule {RuleName} for user {UserId}", rule.RuleName, userId);
                    // continue with next rule
                }
            }
        }

        private async Task ProcessRuleAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            if (rule == null) return;

            switch (rule.RuleName)
            {
                case "Nudge First Upload":
                    await HandleNudgeFirstUploadAsync(user, rule);
                    break;

                case "Stuck Help":
                    await HandleStuckHelpPlanAsync(user, rule);
                    break;
                case "Feature Spotlight":
                    await HandleFeatureSpotlightUploadAsync(user, rule);
                    break;
                case "Social Proof Mid":
                    await HandleSocialProofMidUploadAsync(user, rule);
                    break;
                case "Trial Support":
                    await HandleTrialSupportUploadAsync(user, rule);
                    break;
                case "Post Expire":
                    await HandlePostExpireUploadAsync(user, rule);
                    break;
                case "Trial_Ending":
                    await HandleTrial_EndingUploadAsync(user, rule);
                    break;
                // future rules …
                default:
                    _logger.LogWarning("Unknown rule name: {RuleName}", rule.RuleName);
                    break;
            }
        }

        #region Nudge First Upload
        private async Task HandleNudgeFirstUploadAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            // 1. Skip if user already has a job OR a successful payment
            bool hasJob = await _db.Jobs.AnyAsync(j => j.UserId == user.Id);
            bool hasUpgrade = await _db.PaymentRecords.AnyAsync(p => p.UserId == user.Id && p.Status == "Active" && p.IsTrial == true );

            if (hasJob || hasUpgrade)
            {
                _logger.LogDebug("Skipping NudgeFirstUpload for {Email}: already active", user.Email);
                return;
            }

            await SendFromTemplateAsync(user, rule, new Dictionary<string, string>
            {
                { "{{first_name}}", $"{user.FirstName} {user.LastName}".Trim() },
                { "{{cta_url}}", BuildCallbackUrl("/job-quote") }
            });
        }
        #endregion

        #region Stuck Help
        private async Task HandleStuckHelpPlanAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            // 1. Skip if user already has a job OR a successful payment
            bool hasJob = await _db.Jobs.AnyAsync(j => j.UserId == user.Id);
            bool hasUpgrade = await _db.PaymentRecords.AnyAsync(p => p.UserId == user.Id && p.Status == "Active" && p.IsTrial == true);

            if (hasJob || hasUpgrade)
            {
                _logger.LogDebug("Skipping NudgeFirstUpload for {Email}: already active", user.Email);
                return;
            }

            await SendFromTemplateAsync(user, rule, new Dictionary<string, string>
            {
                { "{{first_name}}", $"{user.FirstName} {user.LastName}".Trim() },
                { "{{cta_url}}", BuildCallbackUrl("/job-quote") }
            });
        }
        #endregion
        #region Feature Spotlight
        private async Task HandleFeatureSpotlightUploadAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            // 1. Skip if user already has a job OR a successful payment
            bool hasJob = await _db.Jobs.AnyAsync(j => j.UserId == user.Id);
            bool hasUpgrade = await _db.PaymentRecords.AnyAsync(p => p.UserId == user.Id && p.Status == "Active" && p.IsTrial == true);

            if (hasJob || hasUpgrade)
            {
                _logger.LogDebug("Skipping NudgeFirstUpload for {Email}: already active", user.Email);
                return;
            }

            await SendFromTemplateAsync(user, rule, new Dictionary<string, string>
            {
                { "{{first_name}}", $"{user.FirstName} {user.LastName}".Trim() },
                { "{{cta_url}}", BuildCallbackUrl("/job-quote") }
            });
        }
        #endregion
        #region Social Proof Mid
        private async Task HandleSocialProofMidUploadAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            // 1. Skip if user already has a job OR a successful payment
            bool hasJob = await _db.Jobs.AnyAsync(j => j.UserId == user.Id);
            bool hasUpgrade = await _db.PaymentRecords.AnyAsync(p => p.UserId == user.Id && p.Status == "Active" && p.IsTrial == true);

            if (hasJob || hasUpgrade)
            {
                _logger.LogDebug("Skipping NudgeFirstUpload for {Email}: already active", user.Email);
                return;
            }

            await SendFromTemplateAsync(user, rule, new Dictionary<string, string>
            {
                { "{{first_name}}", $"{user.FirstName} {user.LastName}".Trim() },
                { "{{cta_url}}", BuildCallbackUrl("/job-quote") }
            });
        }
        #endregion
        #region Trial Support
        private async Task HandleTrialSupportUploadAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            // 1. Skip if user already has a job OR a successful payment
            bool hasJob = await _db.Jobs.AnyAsync(j => j.UserId == user.Id);
            bool hasUpgrade = await _db.PaymentRecords.AnyAsync(p => p.UserId == user.Id && p.Status == "Active" && p.IsTrial == true);

            if (hasJob || hasUpgrade)
            {
                _logger.LogDebug("Skipping NudgeFirstUpload for {Email}: already active", user.Email);
                return;
            }

            await SendFromTemplateAsync(user, rule, new Dictionary<string, string>
            {
                { "{{first_name}}", $"{user.FirstName} {user.LastName}".Trim() },
                { "{{cta_url}}", BuildCallbackUrl("/job-quote") }
            });
        }
        #endregion
        #region Post Expire
        private async Task HandlePostExpireUploadAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            // 1. Skip if user already has a job OR a successful payment
            bool hasJob = await _db.Jobs.AnyAsync(j => j.UserId == user.Id);
            bool hasUpgrade = await _db.PaymentRecords.AnyAsync(p => p.UserId == user.Id && p.Status == "Active" && p.IsTrial == true);

            if (hasJob || hasUpgrade)
            {
                _logger.LogDebug("Skipping NudgeFirstUpload for {Email}: already active", user.Email);
                return;
            }

            await SendFromTemplateAsync(user, rule, new Dictionary<string, string>
            {
                { "{{first_name}}", $"{user.FirstName} {user.LastName}".Trim() },
                { "{{cta_url}}", BuildCallbackUrl("/job-quote") }
            });
        }
        #endregion
        #region Post Expire
        private async Task HandleTrial_EndingUploadAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            // 1. Skip if user already has a job OR a successful payment
            bool hasJob = await _db.Jobs.AnyAsync(j => j.UserId == user.Id);
            bool hasUpgrade = await _db.PaymentRecords.AnyAsync(p => p.UserId == user.Id && p.Status == "Active" && p.IsTrial == true);

            if (hasJob || hasUpgrade)
            {
                _logger.LogDebug("Skipping NudgeFirstUpload for {Email}: already active", user.Email);
                return;
            }

            await SendFromTemplateAsync(user, rule, new Dictionary<string, string>
            {
                { "{{first_name}}", $"{user.FirstName} {user.LastName}".Trim() },
                { "{{cta_url}}", BuildCallbackUrl("/job-quote") }
            });
        }
        #endregion
        #region Helper: load template + replace + send
        private async Task SendFromTemplateAsync(
            UserModel user,
            EmailAutomationRuleModel rule,
            IDictionary<string, string> replacements)
        {
            var template = await _db.EmailTemplates
                .FirstOrDefaultAsync(t => t.TemplateId == rule.TemplateId);

            if (template == null)
            {
                _logger.LogWarning("Template {TemplateId} not found for rule {RuleName}", rule.TemplateId, rule.RuleName);
                return;
            }



            // Always replace Header / Footer first (they exist in every template)
            template.Body = template.Body
                .Replace("{{Header}}", template.HeaderHtml ?? string.Empty)
                .Replace("{{Footer}}", template.FooterHtml ?? string.Empty);

            // Apply user-specific replacements
            foreach (var kvp in replacements)
                template.Body = template.Body.Replace(kvp.Key, kvp.Value ?? string.Empty);

            // Build final email model (adjust to what IEmailSender expects)


            await _emailService.SendEmailAsync(template, user.Email);
            _logger.LogInformation("Sent '{RuleName}' email to {Email} (TemplateId={TemplateId})",
                rule.RuleName, user.Email, rule.TemplateId);
        }
        #endregion

        #region URL builder
        private string BuildCallbackUrl(string path)
        {
            var baseUrl = Environment.GetEnvironmentVariable("FRONTEND_URL")
                          ?? _configuration["FrontEnd:FRONTEND_URL"];

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                _logger.LogWarning("FRONTEND_URL not configured – using fallback");
                baseUrl = "https://app.probuildai.com";
            }

            // Ensure no double slash
            return $"{baseUrl.TrimEnd('/')}{path}";
        }
        #endregion
    }
}