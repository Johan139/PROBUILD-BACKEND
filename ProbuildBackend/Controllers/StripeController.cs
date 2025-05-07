using Elastic.Apm.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;
using Stripe;
using Stripe.Checkout;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StripeController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;

        public StripeController(IConfiguration configuration, ApplicationDbContext context)
        {
            _context = context;
            _configuration = configuration;
        }
        [HttpPost("create-checkout-session")]
        public ActionResult CreateCheckoutSession([FromBody] SubscriptionPaymentRequestDTO request)
        {
            StripeConfiguration.ApiKey = Environment.GetEnvironmentVariable("StripeAPIKey");

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                Mode = "payment",
                LineItems = new List<SessionLineItemOptions>
        {
            new SessionLineItemOptions
            {
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "usd",
                    UnitAmount = (long)(request.Amount * 100),
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = request.PackageName,
                    },
                },
                Quantity = 1,
            }
        },

                // ✅ Store user ID and package in metadata
                Metadata = new Dictionary<string, string>
        {
            { "userId", request.UserId },
            { "package", request.PackageName },
            { "amount", request.Amount.ToString() }
        },

                SuccessUrl = $"http://localhost:4200/payment-success?source={request.Source}",
                CancelUrl = "http://localhost:4200/payment-cancel",
            };

            var service = new SessionService();
            Session session = service.Create(options);

            return Ok(new { url = session.Url });
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> StripeWebhook()    
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

            try
            {
                var stripeEvent = EventUtility.ConstructEvent(
                    json,
                    Request.Headers["Stripe-Signature"],
                    Environment.GetEnvironmentVariable("StripeAPIKeyWH")  // ⬅️ Replace with your real webhook secret
                );

                if (stripeEvent.Type == "checkout.session.completed")
                {
                    var session = stripeEvent.Data.Object as Session;

                    var customerEmail = session.CustomerEmail;
                    var packageName = session.Metadata["package"];
                    var userId = session.Metadata["userId"]; // Add this to metadata on session creation

                    var payment = new PaymentRecord
                    {
                        UserId = userId,
                        Package = packageName,
                        StripeSessionId = session.Id,
                        Status = "Success",
                        PaidAt = DateTime.UtcNow,
                        ValidUntil = DateTime.UtcNow.AddMonths(1),
                        Amount = Convert.ToDecimal(session.AmountTotal) / 100.0m
                    };

                    _context.PaymentRecords.Add(payment);
                    await _context.SaveChangesAsync();
                }

                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Webhook error: {ex.Message}");
                return BadRequest();
            }
        }

        [HttpGet("GetSubscriptions")]
        public async Task<IActionResult> GetSubscriptions()
        {
            try
            {

       
            var subscriptions = await _context.Subscriptions.ToListAsync();
            return Ok(subscriptions);
            }
            catch (Exception ex)
            {

                throw;
            }
        }
    }

    public class PaymentIntentRequest
    {
        public long Amount { get; set; }
        public string Currency { get; set; }
    }
}
