using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using ProbuildBackend.Interface;
using Newtonsoft.Json.Linq;
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
        private readonly PaymentService _paymentService;
        private readonly IEmailSender _emailSender;

        public StripeController(
            IConfiguration configuration,
            ApplicationDbContext context,
            PaymentService paymentService,
            IEmailSender emailSender
        )
        {
            _context = context;
            _configuration = configuration;
            _paymentService = paymentService;
            _emailSender = emailSender;
        }

        [HttpPost("create-checkout-session")]
        public ActionResult CreateCheckoutSession([FromBody] SubscriptionPaymentRequestDTO request)
        {
            string stripePriceId = string.Empty;
            // Validate input
            if (string.IsNullOrEmpty(request.UserId) || string.IsNullOrEmpty(request.PackageName))
            {
                return BadRequest("UserId and PackageName are required.");
            }

            var teamMemberUserId = (
                from tm in _context.TeamMembers
                join u in _context.Users on tm.Email equals u.Email
                where tm.Id == request.AssignedUser
                select u.Id
            ).SingleOrDefault();

            StripeConfiguration.ApiKey =
                Environment.GetEnvironmentVariable("StripeAPIKey")
                ?? _configuration["StripeAPI:StripeKey"];

            StripeModel stripeModel = GetPriceIdForPackage(request.PackageName); // Implement this to map package to Price ID

            if (string.IsNullOrEmpty(stripeModel.StripeProductId))
            {
                return BadRequest("Invalid PackageName.");
            }

            if (request.BillingCycle.ToLower() == "monthly")
            {
                stripePriceId = stripeModel.StripeProductId;
            }
            else
            {
                stripePriceId = stripeModel.StripeProductIdAnually;
            }
            var metadata = new Dictionary<string, string>
{
    { "userId", request.UserId },
    { "package", request.PackageName },
    { "amount", request.Amount.ToString() },
    { "dbTrialRecordId", request.SubscriptionId } // <-- add this
};
            if (!string.IsNullOrWhiteSpace(teamMemberUserId))
            {
                metadata["assignedUser"] = teamMemberUserId;
            }
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                Mode = "subscription",
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions { Price = stripePriceId, Quantity = 1 },
                },

                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string>(metadata),
                },

                SuccessUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? _configuration["FrontEnd:FRONTEND_URL"] + $"/payment-success?source={request.Source}",
                CancelUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? _configuration["FrontEnd:FRONTEND_URL"] + "/payment-cancel",
                Customer = GetOrCreateCustomer(request.UserId), // Ensure this returns a valid Customer ID
                AutomaticTax = new SessionAutomaticTaxOptions { Enabled = false },
            };

            try
            {
                var service = new SessionService();
                Session session = service.Create(options);
                return Ok(new { url = session.Url });
            }
            catch (StripeException ex)
            {
                Console.WriteLine($"Stripe error: {ex.Message}");
                return StatusCode(500, "Error creating checkout session.");
            }
        }

        // Helper method to map package name to Stripe Price ID
        private StripeModel GetPriceIdForPackage(string packageName)
        {
            //Get the stripe product ID for the product ordered
            var Stripeproduct = _context
                .Subscriptions.Where(x => x.Subscription == packageName)
                .FirstOrDefault();

            return Stripeproduct != null
                ? Stripeproduct
                : throw new Exception($"No Price ID found for package: {packageName}");
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            DateTime validDate = DateTime.MinValue;
            int PaymentRecordId = 0;
            try
            {
                var stripeEvent = EventUtility.ConstructEvent(
                    json,
                     Request.Headers["Stripe-Signature"],
                      Environment.GetEnvironmentVariable("StripeWebHookKey") ?? _configuration["StripeAPI:StripeWebHookKey"]
                );

                string userId;
                string packageName;
                decimal amount;
                PaymentRecord SavedPayment = new PaymentRecord();
                switch (stripeEvent.Type)
                {
                    case "customer.subscription.deleted":

                        dynamic CancelledeventData = JsonConvert.DeserializeObject<dynamic>(json);
                        var Cancellationsubscription = stripeEvent.Data.Object as Subscription;
                        // ✅ Subscription ID comes from data.object.id
                        string CancelledsubscriptionId = Cancellationsubscription?.Id;

                        // Optional: userId lives on the subscription metadata (not on lines)
                        var CancelledsubscriptionUserId =
                            Cancellationsubscription?.Metadata != null
                            && Cancellationsubscription.Metadata.TryGetValue("userId", out var uid)
                                ? uid
                                : null;

                        var subscription = await _context
                            .PaymentRecords.Where(s => s.SubscriptionID == CancelledsubscriptionId)
                            .FirstOrDefaultAsync();

                        if (subscription != null)
                        {
                            // Prefer Stripe's timestamps if present; else now
                            DateTime? FromUnix(dynamic v)
                            {
                                if (v == null)
                                    return null;
                                return long.TryParse(v.ToString(), out long secs)
                                    ? DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime
                                    : (DateTime?)null;
                            }

                            var endedAt = FromUnix(Cancellationsubscription?.EndedAt);
                            var canceledAt = FromUnix(Cancellationsubscription?.CancelAt);

                            subscription.Cancelled = true;
                            subscription.CancelledDate = endedAt ?? canceledAt ?? DateTime.UtcNow;
                            subscription.Status = "Cancelled";
                            await _context.SaveChangesAsync();
                        }
                        break;

                    case "invoice.paid":
                        // Deserialize event data
                        var invoices = stripeEvent.Data.Object as Stripe.Invoice;

                        // Subscription id (newer API puts it under parent.subscription_details)
                        var subscriptionId = invoices.Parent.SubscriptionDetails.SubscriptionId;
                        //var lines = invoice?["lines"]?["data"]?.Children<JObject>() ?? Enumerable.Empty<JObject>();
                        var chosenLine =
                            invoices.Lines.FirstOrDefault(li =>
                                li.Parent?.SubscriptionItemDetails?.Proration == false
                            )
                            ?? invoices.Lines.FirstOrDefault(li =>
                                li.Metadata != null && li.Metadata.ContainsKey("userId")
                            )
                            ?? invoices.Lines.FirstOrDefault();
                        string lineDescription = chosenLine?.Description;
                        // --- Line-level metadata (fallback to invoice-level subscription metadata if missing) ---
                        var subscriptionUserId =
                            chosenLine?.Metadata != null
                            && chosenLine.Metadata.TryGetValue("userId", out var uids)
                                ? uids
                                : null;

                        var subscriptionPackageName =
                            chosenLine?.Metadata != null
                            && chosenLine.Metadata.TryGetValue("package", out var p)
                                ? p
                                : null;

                        var amountMeta =
                            chosenLine?.Metadata != null
                            && chosenLine.Metadata.TryGetValue("amount", out var a)
                                ? a
                                : null;
                        var assignedUser =
                            chosenLine?.Metadata != null
                            && chosenLine.Metadata.TryGetValue("assignedUser", out var b)
                                ? b
                                : null;


                        var trialRecordId = chosenLine?.Metadata != null
                                                    && chosenLine.Metadata.TryGetValue("dbTrialRecordId", out var dbTrialId)
                                                        ? dbTrialId
                                                        : null;
                        // After: var invoice = (JObject)root["data"]?["object"];
                        string invoiceNumber = invoices.Number; // fallback to "in_..." if null

                        // --- Other useful line fields ---
                        long totalCents = invoices.Total;

                        //if (string.IsNullOrEmpty(subscriptionId))
                        //{
                        //    subscriptionId = line?.parent?.subscription_item_details?.subscription?.ToString();
                        //}

                        if (
                            string.IsNullOrEmpty(subscriptionUserId)
                            || string.IsNullOrEmpty(subscriptionPackageName)
                        )
                            throw new Exception(
                                "Missing userId or package in subscription metadata"
                            );

                        DateTime subscriptionValidDate = lineDescription.Contains("year")
                            ? DateTime.UtcNow.AddMonths(12)
                            : DateTime.UtcNow.AddMonths(1);

                        decimal subscriptionAmount = totalCents / 100m;

                        // Check if the subscription already exists in PaymentRecords (if it's a renewal)
                        var existingRecord = _context
                            .PaymentRecords.Where(x => x.SubscriptionID == subscriptionId)
                            .ToList();
                        DateTime PaidAt = DateTime.UtcNow;
                        if (existingRecord.Count <= 0)
                        {
                            // No record exists -> First-time subscription, so insert into PaymentRecords
                            SavedPayment = await SavePaymentRecord(
                                subscriptionUserId,
                                subscriptionPackageName,
                                invoiceNumber,
                                subscriptionAmount,
                                subscriptionValidDate,
                                subscriptionId,
                                PaidAt,
                                assignedUser
                            );
                            PaymentRecordId = SavedPayment.Id;

                            // Always insert into PaymentRecordsHistory for both new subscription and renewal
                            await SavePaymentRecordHistory(PaymentRecordId, PaidAt, invoiceNumber, subscriptionAmount, subscriptionValidDate, subscriptionId, subscriptionPackageName);

                            if (!string.IsNullOrEmpty(trialRecordId))
                            {
                                var trial = await _context.PaymentRecords
                                    .Where(x => x.SubscriptionID.ToString() == trialRecordId && x.Status == "Active")
                                    .FirstOrDefaultAsync();

                                if (trial != null)
                                {
                                    trial.Status = "Cancelled";
                                    trial.Cancelled = true;
                                    trial.CancelledDate = DateTime.UtcNow;

                                    _context.PaymentRecords.Update(trial);
                                    await _context.SaveChangesAsync();
                                }
                            }

                            try
                            {
                                // Fetch user
                                var user = await _context.Users.FindAsync(subscriptionUserId);

                                if (user != null)
                                {
                                    var template =
                                        await _context.EmailTemplates.FirstOrDefaultAsync(t =>
                                            t.TemplateName == "ProWelcomeSetup"
                                        );

                                    if (template != null)
                                    {
                                        template.Body = template
                                            .Body.Replace("{{Header}}", template.HeaderHtml ?? "")
                                            .Replace("{{Footer}}", template.FooterHtml ?? "")
                                            .Replace(
                                                "{{first_name}}",
                                                $"{user.FirstName} {user.LastName}".Trim()
                                            )
                                            .Replace(
                                                "{{setup_url}}",
                                                "https://app.probuildai.com/dashboard"
                                            ); // CLICK TARGET

                                        // Send

                                        await _emailSender.SendEmailAsync(template, user.Email);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(
                                    "Failed to send ProWelcomeSetup email: " + ex.Message
                                );
                            }
                        }
                        else
                        {
                            PaymentRecordId = existingRecord[0].Id;
                        }


                        break;

                    default:
                        Console.WriteLine($"Unhandled event type: {stripeEvent.Type}");
                        break;
                }

                return Ok();
            }
            catch (StripeException ex)
            {
                Console.WriteLine($"Stripe error: {ex.Message}");
                return BadRequest();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing webhook: {ex.Message}");
                return StatusCode(500);
            }
        }

        [HttpPost("upgrade-by-package")]
        public async Task<IActionResult> UpgradeByPackageAsync(
            [FromBody] SubscriptionUpgradeDTO payload
        )
        {
            try
            {
                string Amount = string.Empty;
                string stripePriceId = string.Empty;
                if (string.IsNullOrWhiteSpace(payload.subscriptionId) || string.IsNullOrWhiteSpace(payload.packageName))
                    return BadRequest("subscriptionId and packageName are required.");

                var teamMemberUserId =
                    (from tm in _context.TeamMembers
                     join u in _context.Users on tm.Email equals u.Email
                     where tm.Id == payload.AssignedUser
                     select u.Id).SingleOrDefault();

                StripeConfiguration.ApiKey =
                    Environment.GetEnvironmentVariable("StripeAPIKey")
                    ?? _configuration["StripeAPI:StripeKey"];

                // Resolve PRICE (not product)

                StripeModel stripeModel = GetPriceIdForPackage(payload.packageName); // Implement this to map package to Price ID

                if (string.IsNullOrEmpty(stripeModel.StripeProductId))
                {
                    return BadRequest("Invalid PackageName.");
                }

                if (payload.BillingCycle.ToLower() == "monthly")
                {
                    stripePriceId = stripeModel.StripeProductId;
                    Amount = stripeModel.Amount.ToString();
                }
                else
                {
                    stripePriceId = stripeModel.StripeProductIdAnually;
                    Amount = stripeModel.AnnualAmount.ToString();
                }
                if (string.IsNullOrEmpty(stripeModel.StripeProductId))
                    return BadRequest("No active price found for the package.");

                var subs = new Stripe.SubscriptionService();

                // Load subscription
                var sub = await subs.GetAsync(payload.subscriptionId);
                if (sub.Items?.Data?.Count == 0)
                    return BadRequest("No subscription items found.");

                // Subscription item ID (required to change price)
                var itemId = sub.Items.Data[0].Id;

                // Build metadata
                var metadata = new Dictionary<string, string>
        {
            { "userId", payload.userId },
            { "package", payload.packageName },
            { "amount", Amount }
        };

                if (!string.IsNullOrWhiteSpace(teamMemberUserId))
                    metadata["assignedUser"] = teamMemberUserId;

                // Detect current interval (month/year)
                var currentInterval = sub.Items.Data[0].Price.Recurring.Interval;

                // Detect new interval
                var priceService = new PriceService();
                var targetPrice = await priceService.GetAsync(stripePriceId);
                var newInterval = targetPrice.Recurring.Interval;

                // Billing cycle rule:
                // Stripe allows Unchanged ONLY when interval stays the same
                SubscriptionBillingCycleAnchor? anchor = null;

                if (currentInterval == newInterval)
                {
                    anchor = SubscriptionBillingCycleAnchor.Unchanged;
                }
                else
                {
                    anchor = null; // Stripe will reset cycle (required for monthly ↔ yearly changes)
                }

                // Perform subscription update
                var updated = await subs.UpdateAsync(payload.subscriptionId, new SubscriptionUpdateOptions
                {
                    Items = new List<SubscriptionItemOptions>
            {
                new SubscriptionItemOptions
                {
                    Id = itemId,
                    Price = stripePriceId
                }
            },
                    ProrationBehavior = "create_prorations",
                    BillingCycleAnchor = anchor,
                    Metadata = metadata
                });

                // Update your DB
                var paymentRecord = _context.PaymentRecords
                    .Where(x => x.SubscriptionID == payload.subscriptionId)
                    .FirstOrDefault();

                if (paymentRecord != null)
                {
                    paymentRecord.Package = payload.packageName;
                    paymentRecord.Amount = Convert.ToDecimal(Amount);

                    _context.PaymentRecords.Attach(paymentRecord);
                    _context.Entry(paymentRecord).Property(u => u.Package).IsModified = true;
                    _context.Entry(paymentRecord).Property(u => u.Amount).IsModified = true;
                    _context.SaveChanges();
                }

                return Ok(new
                {
                    status = "ok",
                    subscriptionId = updated.Id
                });
            }
            catch (Exception ex)
            {
                throw;
            }
        }


        // Shared method to save payment record
        private async Task<PaymentRecord> SavePaymentRecord(
            string userId,
            string packageName,
            string sessionId,
            decimal amount,
            DateTime validDate,
            string SubscriptionID,
            DateTime PaidAt,
            string assignedUser
        )
        {
            var payment = new PaymentRecord
            {
                UserId = userId,
                Package = packageName,
                StripeSessionId = sessionId,
                Status = "Active",
                PaidAt = PaidAt,
                ValidUntil = validDate,
                Amount = amount,
                IsTrial = false,
                SubscriptionID = SubscriptionID,
                AssignedUser = assignedUser,
            };

            _context.PaymentRecords.Add(payment);
            await _context.SaveChangesAsync();

            return payment;
        }

        private async Task SavePaymentRecordHistory(
            int PaymentRecordId,
            DateTime PaidAt,
            string sessionId,
            decimal amount,
            DateTime validDate,
            string SubscriptionID,
            string subscriptionPackageName
        )
        {
            try
            {
                var paymentHistory = new PaymentRecordHistoryModel
                {
                    PaymentRecordId = PaymentRecordId,
                    PaidAt = PaidAt,
                    StripeSessionId = sessionId,
                    Status = "Success",
                    ValidUntil = validDate,
                    Amount = amount,
                    PackageName = subscriptionPackageName,
                };

                _context.PaymentRecordsHistory.Add(paymentHistory);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                throw;
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

        // Helper method to get or create a Stripe Customer
        private string GetOrCreateCustomer(string userId)
        {
            var customerService = new CustomerService();

            // Check if customer exists (you may store Stripe Customer ID in your DB)
            // Example: Query your database for userId to get Stripe Customer ID
            string StripeCustomerIdFromDB = GetCustomerIdFromDatabase(userId); // Implement this
            if (string.IsNullOrEmpty(StripeCustomerIdFromDB))
            {
                // Create a new customer in Stripe
                var customerOptions = new CustomerCreateOptions
                {
                    Metadata = new Dictionary<string, string> { { "userId", userId } },
                    // Optionally add email or other details
                    // Email = request.Email,
                };
                var customer = customerService.Create(customerOptions);
                StripeCustomerIdFromDB = customer.Id;

                // Save customerId to your database for future reference
                SaveCustomerIdToDatabase(userId, StripeCustomerIdFromDB); // Implement this
            }

            return StripeCustomerIdFromDB;
        }

        public string GetCustomerIdFromDatabase(string userId)
        {
            try
            {
                var customerId = _context.Users.Where(x => x.Id == userId).FirstOrDefault();
                if (customerId == null)
                {
                    return "User not found";
                }
                return customerId.StripeCustomerId;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public void SaveCustomerIdToDatabase(string userId, string customerId)
        {
            try
            {
                var users = _context.Users.Where(x => x.Id == userId).FirstOrDefault();
                users.StripeCustomerId = customerId;
                _context.Users.Attach(users);
                _context.Entry(users).Property(u => u.StripeCustomerId).IsModified = true;
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        [HttpPost("cancelsubscription/{subscriptionId}")]
        public async Task<IActionResult> CancelSubscriptionByEmail(string subscriptionId)
        {
            if (string.IsNullOrWhiteSpace(subscriptionId))
                return BadRequest("subscriptionId is required.");

            try
            {
                StripeConfiguration.ApiKey =
                    Environment.GetEnvironmentVariable("StripeAPIKey")
                    ?? _configuration["StripeAPI:StripeKey"];
                // Step 1: Get subscription from your database
                var subscription = await _context
                    .PaymentRecords.Where(s =>
                        s.SubscriptionID == subscriptionId && s.Status == "Active"
                    )
                    .FirstOrDefaultAsync();

                // Step 2: Cancel subscription on Stripe
                var stripeService = new Stripe.SubscriptionService();
                //var cancelOptions = new SubscriptionCancelOptions
                //{
                //    InvoiceNow = false,
                //    Prorate = false
                //};
                var options = new SubscriptionUpdateOptions { CancelAtPeriodEnd = true };
                //var stripeResult = await stripeService.CancelAsync(subscription.SubscriptionID, cancelOptions);
                if (subscription.Amount == 0M)
                {
                    subscription.Status = "Cancelled";
                    subscription.Cancelled = true;
                    subscription.CancelledDate = DateTime.UtcNow;
                    _context.PaymentRecords.Attach(subscription);
                    _context.Entry(subscription).Property(u => u.Status).IsModified = true;
                    _context.Entry(subscription).Property(u => u.Cancelled).IsModified = true;
                    _context.Entry(subscription).Property(u => u.CancelledDate).IsModified = true;
                    _context.SaveChanges();
                }
                else
                {
                    var updated = await stripeService.UpdateAsync(
                        subscription.SubscriptionID,
                        options
                    );
                }
                // Step 3: Update your DB

                return Ok("Subscription cancelled successfully");
            }
            catch (StripeException ex)
            {
                return StatusCode(500, new { message = $"Stripe error: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Unexpected error: {ex.Message}" });
            }
        }

        [HttpPost("preview-upgrade")]
        public async Task<ActionResult<ProrationPreviewDto>> PreviewUpgrade(
            [FromBody] UpgradePreviewRequest req
        )
        {
            try
            {

                string productPriceId = string.Empty;
            StripeConfiguration.ApiKey = Environment.GetEnvironmentVariable("StripeAPIKey")
                                          ?? _configuration["StripeAPI:StripeKey"];

                // 1) Resolve the target Price ID from the selected package value
                //    (Your method must return a PRICE id like "price_...", not a product id)
                var target = GetPriceIdForPackage(req.PackageName);
                if (string.IsNullOrWhiteSpace(target?.StripeProductId))
                    return BadRequest("Unknown package/price mapping.");


            if(req.BillingCycle == "yearly")
                {
                    productPriceId = target.StripeProductIdAnually;
                }
            else
                {
                    productPriceId = target.StripeProductId;
                }
                    // 2) Load the subscription to infer customer + existing item
                    var subSvc = new Stripe.SubscriptionService();
            var subscription = await subSvc.GetAsync(
                req.SubscriptionId,
                new SubscriptionGetOptions
                {
                    // expand if you need product info to choose which line to replace
                    Expand = new List<string> { "items.data.price.product" }
                }
            );
            if (subscription == null) return NotFound("Subscription not found.");

                var customerId = subscription.CustomerId;
                // Pick the primary item; adapt if you have multiple items per sub
                var existingItem = subscription.Items?.Data?.FirstOrDefault();
                if (existingItem == null)
                    return BadRequest("Subscription has no items to replace.");

                // 3) Proration date -> DateTime? for Stripe .NET
                var prorationUnix =
                    req.ProrationDateUnix ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var prorationInstant = DateTimeOffset
                    .FromUnixTimeSeconds(prorationUnix)
                    .UtcDateTime;

            // 4) Build invoice preview with proration
            var invoiceSvc = new InvoiceService();
            var preview = await invoiceSvc.CreatePreviewAsync(new InvoiceCreatePreviewOptions
            {
                Customer = customerId,
                Subscription = req.SubscriptionId,
                SubscriptionDetails = new InvoiceSubscriptionDetailsOptions
                {
                    ProrationBehavior = "create_prorations",
                    ProrationDate = prorationInstant,
                    BillingCycleAnchor = InvoiceSubscriptionDetailsBillingCycleAnchor.Unchanged,
                    Items = new List<InvoiceSubscriptionDetailsItemOptions>
            {
                new()
                {
                    Id = existingItem.Id,                   // replace current item
                    Price = productPriceId          // with new PRICE id
                }
            }
                }
            });

                // 5) Map proration lines
                var lines = preview
                    .Lines.Data.Where(li => li.Parent?.SubscriptionItemDetails?.Proration == true)
                    .Select(li => new ProrationPreviewLineDto
                    {
                        Description = li.Description ?? string.Empty,
                        Amount = (li.Amount) / 100m,
                    })
                    .ToList();

                // 6) Response for your Angular dialog
                var dto = new ProrationPreviewDto
                {
                    ProrationDateUnix = prorationUnix,
                    Currency = preview.Currency,
                    ProrationSubtotal = lines.Sum(x => x.Amount),
                    PreviewTotal = (preview.Total) / 100m,
                    NextBillingDate = preview.PeriodEnd.ToUniversalTime(),
                    ProrationLines = lines,
                };

                return Ok(dto);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        [HttpPost("process-finders-fee")]
        public async Task<IActionResult> ProcessFindersFee([FromBody] FindersFeeRequestDTO request)
        {
            if (
                request == null
                || string.IsNullOrEmpty(request.UserId)
                || request.WinningBidAmount <= 0
            )
            {
                return BadRequest("Invalid request data.");
            }

            try
            {
                var charge = await _paymentService.ProcessFindersFee(
                    request.UserId,
                    request.WinningBidAmount,
                    request.JobId
                );
                if (charge == null)
                {
                    return Ok("No finders fee required for this user type.");
                }
                return Ok(charge);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error processing finders fee: {ex.Message}");
            }
        }
        [HttpGet("user-subscriptions/{userId}")]
        public async Task<IActionResult> GetUserStripeSubscriptions(string userId)
        {
            try
            {
                var normUserId = (userId ?? "").Trim().ToLower();

                // Get user's email
                var viewerEmail = await _context.Users.AsNoTracking()
                    .Where(u => (u.Id ?? "").ToLower() == normUserId)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync();

                var normEmail = (viewerEmail ?? "").Trim().ToLower();

                // Load ALL payment records
                var records = await _context.PaymentRecords.AsNoTracking()
                    .Where(pr =>
                        ((pr.UserId ?? "").ToLower().Trim() == normUserId) ||
                        ((pr.AssignedUser ?? "").ToLower().Trim() == normUserId) ||
                        (normEmail != "" && ((pr.AssignedUser ?? "").ToLower().Trim() == normEmail))
                    )
                    .ToListAsync();

                if (records.Count == 0)
                    return Ok(new List<object>());

                StripeConfiguration.ApiKey =
                    Environment.GetEnvironmentVariable("StripeAPIKey")
                    ?? _configuration["StripeAPI:StripeKey"];

                var stripe = new Stripe.SubscriptionService();
                var result = new List<object>();

                foreach (var rec in records)
                {
                    bool isStripeSub = rec.SubscriptionID != null &&
                                       rec.SubscriptionID.StartsWith("sub_");

                    //
                    // NON-STRIPE OR MANUAL RECORD → return DB info only
                    //
                    if (!isStripeSub)
                    {
                        long? validUntilUnix =
                            rec.ValidUntil == DateTime.MinValue
                                ? (long?)null
                                : new DateTimeOffset(rec.ValidUntil).ToUnixTimeSeconds();

                        result.Add(new
                        {
                            subscriptionId = rec.SubscriptionID,
                            package = rec.Package,
                            amount = rec.Amount,
                            status = rec.Status,
                            cancel_at_period_end = false,
                            current_period_end = validUntilUnix,
                            validUntil = validUntilUnix,
                            assignedUser = rec.AssignedUser
                        });

                        continue;
                    }

                    //
                    // STRIPE SUB – SAFE FETCH
                    //
                    Subscription sub = null!;
                    long? periodEndUnix = null;

                    try
                    {
                        sub = await stripe.GetAsync(
                            rec.SubscriptionID,
                            new SubscriptionGetOptions
                            {
                                Expand = new List<string>
                                {
                            "latest_invoice",
                            "latest_invoice.lines",
                            "items",
                            "items.data"
                                }
                            }
                        );

                        dynamic raw = Newtonsoft.Json.Linq.JObject.Parse(sub.ToJson());
                        periodEndUnix = (long?)raw["items"]?["data"]?[0]?["current_period_end"];
                    }
                    catch (StripeException ex)
                    {
                        // 🔥 STRIPE SAYS SUB DOES NOT EXIST / WAS DELETED / INVALID
                        result.Add(new
                        {
                            subscriptionId = rec.SubscriptionID,
                            package = rec.Package,
                            amount = rec.Amount,
                            status = "not_found_on_stripe",
                            cancel_at_period_end = false,
                            current_period_end = (long?)null,
                            validUntil = (long?)null,
                            assignedUser = rec.AssignedUser,
                            message = $"Stripe error: {ex.Message}"
                        });

                        continue; // do NOT break the loop
                    }
                    catch (Exception ex)
                    {
                        // 🔥 Other unexpected error – still do NOT break entire endpoint
                        result.Add(new
                        {
                            subscriptionId = rec.SubscriptionID,
                            package = rec.Package,
                            amount = rec.Amount,
                            status = "error_fetching_from_stripe",
                            cancel_at_period_end = false,
                            current_period_end = (long?)null,
                            validUntil = (long?)null,
                            assignedUser = rec.AssignedUser,
                            message = ex.Message
                        });

                        continue;
                    }

                    //
                    // SUCCESSFUL STRIPE FETCH
                    //
                    result.Add(new
                    {
                        subscriptionId = rec.SubscriptionID,
                        package = rec.Package,
                        amount = rec.Amount,
                        status = sub.Status,
                        cancel_at_period_end = sub.CancelAtPeriodEnd,
                        current_period_end = periodEndUnix,
                        validUntil = periodEndUnix,
                        assignedUser = rec.AssignedUser
                    });
                }

                return Ok(result);
            }
            catch
            {
                return StatusCode(500, "Failed to load subscriptions.");
            }
        }


        [HttpPost("undo-cancellation/{subscriptionId}")]
        public async Task<IActionResult> UndoCancellation(string subscriptionId)
        {
            if (string.IsNullOrWhiteSpace(subscriptionId))
                return BadRequest("SubscriptionId is required.");

            try
            {
                StripeConfiguration.ApiKey =
                    Environment.GetEnvironmentVariable("StripeAPIKey")
                    ?? _configuration["StripeAPI:StripeKey"];

                var service = new Stripe.SubscriptionService();

                var options = new SubscriptionUpdateOptions
                {
                    CancelAtPeriodEnd = false
                };

                var updated = await service.UpdateAsync(subscriptionId, options);

                // --- Update your DB ---
                var subscription = await _context.PaymentRecords
                    .Where(s => s.SubscriptionID == subscriptionId)
                    .FirstOrDefaultAsync();

                if (subscription != null)
                {
                    subscription.Cancelled = false;
                    subscription.Status = "Active";
                    subscription.CancelledDate = null;

                    _context.PaymentRecords.Attach(subscription);
                    _context.Entry(subscription).Property(x => x.Cancelled).IsModified = true;
                    _context.Entry(subscription).Property(x => x.Status).IsModified = true;
                    _context.Entry(subscription).Property(x => x.CancelledDate).IsModified = true;
                    await _context.SaveChangesAsync();
                }

                return Ok(new
                {
                    message = "Subscription cancellation undone.",
                    subscriptionId = updated.Id,
                    cancelAtPeriodEnd = updated.CancelAtPeriodEnd
                });
            }
            catch (StripeException ex)
            {
                return StatusCode(500, new { message = $"Stripe error: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Unexpected error: {ex.Message}" });
            }
        }

    }



    public class PaymentIntentRequest
    {
        public long Amount { get; set; }
        public string Currency { get; set; }
    }

    public class FindersFeeRequestDTO
    {
        public string UserId { get; set; }
        public decimal WinningBidAmount { get; set; }
        public string JobId { get; set; }
    }
}
