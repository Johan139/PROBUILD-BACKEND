using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Models;
using IEmailSender = ProbuildBackend.Interface.IEmailSender;
namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubscriptionEmailController : ControllerBase
    {
        private readonly IEmailSender _emailService;

        public SubscriptionEmailController(IEmailSender emailService)
        {
            _emailService = emailService;
        }

        [HttpPost("send-subscription-confirmation")]
        public async Task<IActionResult> SendSubscriptionConfirmation([FromBody] SubscriptionModel data)
        {
            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "subscription-confirmation.html");
            var templateContent = await System.IO.File.ReadAllTextAsync(templatePath);

            // Replace placeholders with actual values
            string html = templateContent
                .Replace("{{client_name}}", data.ClientName)
                .Replace("{{contact_name}}", data.ContactName)
                .Replace("{{contact_email}}", data.ContactEmail)
                .Replace("{{plan_type}}", data.PlanType)
                .Replace("{{plan_price}}", data.PlanPrice)
                .Replace("{{start_date}}", data.StartDate)
                .Replace("{{next_billing_date}}", data.NextBillingDate)
                .Replace("{{seats}}", data.Seats.ToString())
                .Replace("{{platform_tier}}", data.PlatformTier)
                .Replace("{{custom_terms}}", data.CustomTerms)
                .Replace("{{promo_code}}", data.PromoCode)
                .Replace("{{tax}}", data.Tax)
                .Replace("{{total_charged}}", data.TotalCharged)
                .Replace("{{payment_method}}", data.PaymentMethod);

           // await _emailService.SendEmailAsync(data.ContactEmail, "Your ProBuild AI Subscription Confirmation", html);

            return Ok();
        }
    }
}