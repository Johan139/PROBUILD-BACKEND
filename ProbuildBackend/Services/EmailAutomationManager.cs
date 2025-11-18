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

        // ---------------------------------------------------------------------
        // MAIN EXECUTION ENTRY POINT (kept identical)
        // ---------------------------------------------------------------------
        public async Task ExecuteAutomationAsync(string userId, int ruleId)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return;

            var rule = await _db.EmailAutomationRules.FindAsync(ruleId);
            if (rule == null || !rule.IsActive) return;

            try
            {
                await ProcessRuleAsync(user, rule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process rule {RuleId} for user {UserId}", ruleId, userId);
            }
        }

        // ---------------------------------------------------------------------
        // RULE DISPATCHER (unchanged)
        // ---------------------------------------------------------------------
        private async Task ProcessRuleAsync(UserModel user, EmailAutomationRuleModel rule)
        {
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

                case "Trial_Ending":
                    await HandleTrial_EndingUploadAsync(user, rule);
                    break;
                case "Post Expire":
                    await HandlePostExpireAsync(user, rule);
                    break;
                default:
                    _logger.LogWarning("Unknown rule {RuleName}", rule.RuleName);
                    break;
            }
        }

        // ---------------------------------------------------------------------
        // HELPER: GET USER TRIAL (existing tables only)
        // ---------------------------------------------------------------------
        private async Task<PaymentRecord?> GetUserTrial(UserModel user)
        {
            return await _db.PaymentRecords
                .Where(p => p.UserId == user.Id && p.IsTrial == true)
                .OrderByDescending(p => p.PaidAt)
                .FirstOrDefaultAsync();
        }

        // ---------------------------------------------------------------------
        // HELPER: COMMON CONDITION BLOCKS
        // ---------------------------------------------------------------------
        private async Task<bool> UserConverted(UserModel user)
        {
            return await _db.PaymentRecords.AnyAsync(p =>
                p.UserId == user.Id &&
                p.Status == "Active" &&
                p.IsTrial == false &&
                p.Amount != 0M);
        }

        private async Task<bool> UserExpired(UserModel user)
        {
            var trial = await GetUserTrial(user);
            return trial != null && trial.ValidUntil < DateTime.UtcNow;
        }

        private async Task<bool> UserUploadedBlueprint(UserModel user)
        {
            return await _db.Jobs.AnyAsync(j => j.UserId == user.Id);
        }

        private async Task<bool> UserEngaged(UserModel user)
        {
            return await _db.Jobs.CountAsync(j => j.UserId == user.Id) >= 2;
        }

        private async Task<bool> NoRecentActivity48h(UserModel user)
        {
            var since = DateTime.UtcNow.AddHours(-48);
            return !await _db.Jobs.AnyAsync(j => j.UserId == user.Id && j.CreatedAt >= since);
        }

        // ---------------------------------------------------------------------
        // 1. Nudge First Upload (12h)
        // ---------------------------------------------------------------------
        private async Task HandleNudgeFirstUploadAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            bool hasBlueprint = await UserUploadedBlueprint(user);
            bool converted = await UserConverted(user);

            if (hasBlueprint || converted)
                return;

            await SendFromTemplate(user, rule);
        }

        // ---------------------------------------------------------------------
        // 2. Stuck Help (48h)
        // ---------------------------------------------------------------------
        private async Task HandleStuckHelpPlanAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            bool engaged = await UserEngaged(user);
            bool converted = await UserConverted(user);
            bool expired = await UserExpired(user);
            bool noRecentActivity = await NoRecentActivity48h(user);

            if (engaged || converted || expired || !noRecentActivity)
                return;

            await SendFromTemplate(user, rule);
        }

        // ---------------------------------------------------------------------
        // 3. Feature Spotlight (≈72h)
        // ACTIVATED = uploaded ≥ 1 blueprint
        // ---------------------------------------------------------------------
        private async Task HandleFeatureSpotlightUploadAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            bool hasBlueprint = await UserUploadedBlueprint(user);  // ACTIVATED
            bool engaged = await UserEngaged(user);                 // ENGAGED
            bool converted = await UserConverted(user);             // CONVERTED
            //bool expired = await UserExpired(user);                 // EXPIRED

            // Only send if ACTIVATED but NOT ENGAGED, NOT CONVERTED
            if (!hasBlueprint || engaged || converted)
                return;

            await SendFromTemplate(user, rule);
        }

        // ---------------------------------------------------------------------
        // 4. Social Proof Mid (Day 5)
        // ---------------------------------------------------------------------
        private async Task HandleSocialProofMidUploadAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            bool converted = await UserConverted(user);
            bool expired = await UserExpired(user);

            if (converted || expired)
                return;

            await SendFromTemplate(user, rule);
        }
        private async Task HandlePostExpireAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            var trial = await GetUserTrial(user);
            if (trial == null)
                return;

            bool converted = await UserConverted(user);
            if (converted)
                return;

            bool expired = trial.ValidUntil < DateTime.UtcNow;
            if (!expired)
                return;

            // Calculate when the rule SHOULD fire
            var targetDate = trial.ValidUntil.AddHours(rule.DelayHours);

            // Only fire if we are "in window" (within 1 hour)
            if (DateTime.UtcNow < targetDate || DateTime.UtcNow > targetDate.AddHours(1))
                return;

            // Good: trial expired AND this is the correct morning window
            await SendFromTemplate(user, rule);
        }


        // ---------------------------------------------------------------------
        // 5. Trial Support (Day 6–7)
        // ---------------------------------------------------------------------
        private async Task HandleTrialSupportUploadAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            bool converted = await UserConverted(user);
            bool expired = await UserExpired(user);

            if (converted || expired)
                return;

            await SendFromTemplate(user, rule);
        }

        // ---------------------------------------------------------------------
        // 6. Trial Ending (~Day 7 @ 18:00)
        // ---------------------------------------------------------------------
        private async Task HandleTrial_EndingUploadAsync(UserModel user, EmailAutomationRuleModel rule)
        {
            var trial = await GetUserTrial(user);
            if (trial == null)
                return;

            bool converted = await UserConverted(user);
            bool expired = trial.ValidUntil < DateTime.UtcNow;

            if (converted || expired)
                return;

            bool endsSoon =
                (trial.ValidUntil - DateTime.UtcNow) <= TimeSpan.FromHours(12) &&
                (trial.ValidUntil - DateTime.UtcNow) >= TimeSpan.Zero;

            if (!endsSoon)
                return;

            await SendFromTemplate(user, rule);
        }

        // ---------------------------------------------------------------------
        // SENDING TEMPLATE (unchanged)
        // ---------------------------------------------------------------------
        private async Task SendFromTemplate(UserModel user, EmailAutomationRuleModel rule)
        {
            var template = await _db.EmailTemplates
                .FirstOrDefaultAsync(t => t.TemplateId == rule.TemplateId);

            if (template == null)
            {
                _logger.LogWarning("Template {TemplateId} not found for rule {RuleName}",
                    rule.TemplateId, rule.RuleName);
                return;
            }

            template.Body = template.Body
            .Replace("{{Header}}", template.HeaderHtml ?? "")
            .Replace("{{Footer}}", template.FooterHtml ?? "")
            .Replace("{{first_name}}", $"{user.FirstName} {user.LastName}".Trim())
            .Replace("{{cta_url}}", BuildCallbackUrl(rule.CtaUrl))
            .Replace("{{book_link}}", BuildCallbackUrl(rule.BookLink))
            .Replace("{{upgrade_url}}", BuildCallbackUrl(rule.UpgradeUrl));
            await _emailService.SendEmailAsync(template, user.Email);

            _logger.LogInformation("Sent automation email '{RuleName}' to {Email}",
                rule.RuleName, user.Email);
        }

        // ---------------------------------------------------------------------
        // URL BUILDER
        // ---------------------------------------------------------------------
        private string BuildCallbackUrl(string path)
        {
            var baseUrl = Environment.GetEnvironmentVariable("FRONTEND_URL")
                          ?? _configuration["FrontEnd:FRONTEND_URL"]
                          ?? "https://app.probuildai.com";

            return $"{baseUrl.TrimEnd('/')}{path}";
        }
    }
}
