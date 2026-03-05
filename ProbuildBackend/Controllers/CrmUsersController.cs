using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CrmUsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly IConfiguration _configuration;
        private readonly IEmailTemplateService _emailTemplate;
        private readonly IEmailSender _emailSender;

        public CrmUsersController(
            ApplicationDbContext context,
            IDataProtectionProvider dataProtectionProvider,
            IConfiguration configuration,
            IEmailTemplateService emailTemplate,
            IEmailSender emailSender)
        {
            _context = context;
            _dataProtectionProvider = dataProtectionProvider;
            _configuration = configuration;
            _emailTemplate = emailTemplate;
            _emailSender = emailSender;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<CrmUserDetailsDto>> GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Id parameter cannot be null or empty.");

            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
                return NotFound("User not found.");

            var subscription = await GetSubscriptionSummaryInternal(id);

            var dto = new CrmUserDetailsDto
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                EmailConfirmed = user.EmailConfirmed,
                PhoneNumber = user.PhoneNumber,
                CountryNumberCode = user.CountryNumberCode,
                UserType = user.UserType,
                IsAdmin = user.IsAdmin,
                CompanyName = user.CompanyName,
                CompanyRegNo = user.CompanyRegNo,
                VatNo = user.VatNo,
                Trade = user.Trade,
                SupplierType = user.SupplierType,
                SubscriptionPackage = user.SubscriptionPackage,
                Country = user.Country,
                State = user.State,
                City = user.City,
                IsActive = user.IsActive,
                IsVerified = user.IsVerified,
                Subscription = subscription,
            };

            return Ok(dto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] CrmUserUpdateDto model)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Id parameter cannot be null or empty.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
                return NotFound("User not found.");

            if (model.Email != null)
            {
                var email = model.Email.Trim();
                user.Email = email;
                user.UserName = email;
                user.NormalizedEmail = email.ToUpperInvariant();
                user.NormalizedUserName = email.ToUpperInvariant();
            }

            if (model.EmailConfirmed.HasValue) user.EmailConfirmed = model.EmailConfirmed.Value;

            if (model.FirstName != null) user.FirstName = model.FirstName;
            if (model.LastName != null) user.LastName = model.LastName;
            if (model.PhoneNumber != null) user.PhoneNumber = model.PhoneNumber;
            if (model.CountryNumberCode != null) user.CountryNumberCode = model.CountryNumberCode;
            if (model.UserType != null) user.UserType = model.UserType;
            if (model.IsAdmin.HasValue) user.IsAdmin = model.IsAdmin.Value;
            if (model.CompanyName != null) user.CompanyName = model.CompanyName;
            if (model.CompanyRegNo != null) user.CompanyRegNo = model.CompanyRegNo;
            if (model.VatNo != null) user.VatNo = model.VatNo;
            if (model.Trade != null) user.Trade = model.Trade;
            if (model.SupplierType != null) user.SupplierType = model.SupplierType;
            if (model.SubscriptionPackage != null) user.SubscriptionPackage = model.SubscriptionPackage;
            if (model.Country != null) user.Country = model.Country;
            if (model.State != null) user.State = model.State;
            if (model.City != null) user.City = model.City;
            if (model.IsActive.HasValue) user.IsActive = model.IsActive.Value;
            if (model.IsVerified.HasValue) user.IsVerified = model.IsVerified.Value;

            var hasAddressUpdate =
                model.FormattedAddress != null
                || model.GooglePlaceId != null
                || model.StreetNumber != null
                || model.StreetName != null
                || model.City != null
                || model.State != null
                || model.PostalCode != null
                || model.Country != null
                || model.CountryCode != null
                || model.Latitude.HasValue
                || model.Longitude.HasValue
                || model.AddressType != null;

            if (hasAddressUpdate)
            {
                var address = await _context.UserAddress
                    .Where(a => a.UserId == id && a.Deleted != true)
                    .OrderByDescending(a => a.UpdatedAt)
                    .ThenByDescending(a => a.CreatedAt)
                    .FirstOrDefaultAsync();

                if (address == null)
                {
                    address = new UserAddressModel
                    {
                        UserId = id,
                        CreatedAt = DateTime.UtcNow,
                    };
                    _context.UserAddress.Add(address);
                }

                if (model.StreetNumber != null) address.StreetNumber = model.StreetNumber;
                if (model.StreetName != null) address.StreetName = model.StreetName;
                if (model.City != null) address.City = model.City;
                if (model.State != null) address.State = model.State;
                if (model.PostalCode != null) address.PostalCode = model.PostalCode;
                if (model.Country != null) address.Country = model.Country;
                if (model.CountryCode != null) address.CountryCode = model.CountryCode;
                if (model.FormattedAddress != null) address.FormattedAddress = model.FormattedAddress;
                if (model.GooglePlaceId != null) address.GooglePlaceId = model.GooglePlaceId;
                if (model.Latitude.HasValue) address.Latitude = model.Latitude;
                if (model.Longitude.HasValue) address.Longitude = model.Longitude;
                if (model.AddressType != null) address.AddressType = model.AddressType;

                address.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "User updated successfully." });
        }

        [HttpPost("{id}/password-reset")]
        public async Task<IActionResult> SendPasswordReset(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Id parameter cannot be null or empty.");

            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
                return NotFound("User not found.");

            if (string.IsNullOrWhiteSpace(user.Email))
                return BadRequest("User email is missing.");

            var protector = _dataProtectionProvider
                .CreateProtector($"{user.Id}:Default:ResetPassword")
                .ToTimeLimitedDataProtector();

            var token = protector.Protect(
                "ResetToken:" + Guid.NewGuid(),
                lifetime: TimeSpan.FromMinutes(15)
            );

            var frontendBaseUrl =
                Environment.GetEnvironmentVariable("FRONTEND_URL")
                ?? _configuration["FrontEnd:FRONTEND_URL"];

            var callbackUrl =
                $"{frontendBaseUrl}/reset-password?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}";

            var template = await _emailTemplate.GetTemplateAsync("PasswordResetEmail");

            template.Body = template.Body
                .Replace("{{ResetLink}}", callbackUrl)
                .Replace("{{UserName}}", $"{user.FirstName} {user.LastName}".Trim())
                .Replace("{{Header}}", template.HeaderHtml)
                .Replace("{{Footer}}", template.FooterHtml);

            await _emailSender.SendEmailAsync(template, user.Email);

            return Ok(new { message = "Password reset email sent." });
        }

        [HttpGet("{id}/address")]
        public async Task<ActionResult<UserAddressModel?>> GetPrimaryAddress(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Id parameter cannot be null or empty.");

            var userExists = await _context.Users.AsNoTracking().AnyAsync(u => u.Id == id);
            if (!userExists)
                return NotFound("User not found.");

            var address = await _context.UserAddress
                .AsNoTracking()
                .Where(a => a.UserId == id && a.Deleted != true)
                .OrderByDescending(a => a.UpdatedAt)
                .ThenByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            return Ok(address);
        }

        [HttpPut("{id}/address")]
        public async Task<ActionResult<UserAddressModel>> UpsertPrimaryAddress(
            string id,
            [FromBody] UserAddressDTO address)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Id parameter cannot be null or empty.");

            var userExists = await _context.Users.AsNoTracking().AnyAsync(u => u.Id == id);
            if (!userExists)
                return NotFound("User not found.");

            var existing = await _context.UserAddress
                .Where(a => a.UserId == id && a.Deleted != true)
                .OrderByDescending(a => a.UpdatedAt)
                .ThenByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            if (existing == null)
            {
                existing = new UserAddressModel
                {
                    UserId = id,
                    CreatedAt = DateTime.UtcNow,
                };
                _context.UserAddress.Add(existing);
            }

            existing.StreetNumber = address.StreetNumber;
            existing.StreetName = address.StreetName;
            existing.City = address.City;
            existing.State = address.State;
            existing.PostalCode = address.PostalCode;
            existing.Country = address.Country;
            existing.CountryCode = address.CountryCode;
            existing.FormattedAddress = address.FormattedAddress;
            existing.GooglePlaceId = address.GooglePlaceId;
            existing.Latitude = address.Latitude;
            existing.Longitude = address.Longitude;
            existing.AddressType = address.AddressType;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(existing);
        }

        [HttpGet("{id}/subscription")]
        public async Task<ActionResult<CrmUserSubscriptionSummaryDto>> GetSubscriptionSummary(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Id parameter cannot be null or empty.");

            var userExists = await _context.Users.AsNoTracking().AnyAsync(u => u.Id == id);
            if (!userExists)
                return NotFound("User not found.");

            var summary = await GetSubscriptionSummaryInternal(id);
            return Ok(summary);
        }

        [HttpGet("subscription-packages")]
        public async Task<IActionResult> GetSubscriptionPackages()
        {
            var packages = await _context.Subscriptions
                .AsNoTracking()
                .Select(s => new
                {
                    name = s.Subscription,
                    amount = s.Amount,
                    annualAmount = s.AnnualAmount,
                    stripeProductId = s.StripeProductId,
                    stripeProductIdAnnually = s.StripeProductIdAnually,
                })
                .ToListAsync();

            static int GetOrder(string? name)
            {
                if (string.IsNullOrWhiteSpace(name)) return 999;
                var n = name.Trim();

                if (n.StartsWith("Basic", StringComparison.OrdinalIgnoreCase)) return 0;
                if (n.StartsWith("Trial", StringComparison.OrdinalIgnoreCase)) return 1;
                if (string.Equals(n, "Essential", StringComparison.OrdinalIgnoreCase)) return 2;
                if (string.Equals(n, "Advance", StringComparison.OrdinalIgnoreCase)) return 3;
                if (string.Equals(n, "Premium", StringComparison.OrdinalIgnoreCase)) return 4;

                return 998;
            }

            packages = packages
                .OrderBy(p => GetOrder(p.name))
                .ThenBy(p => p.name)
                .ToList();

            return Ok(packages);
        }

        private async Task<CrmUserSubscriptionSummaryDto> GetSubscriptionSummaryInternal(string userId)
        {
            var now = DateTime.UtcNow;

            var record = await _context.PaymentRecords
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.ValidUntil)
                .ThenByDescending(p => p.PaidAt)
                .FirstOrDefaultAsync();

            if (record == null)
            {
                return new CrmUserSubscriptionSummaryDto
                {
                    HasActiveSubscription = false,
                    Status = "None",
                };
            }

            var hasActive = (record.Status == "Active") && record.ValidUntil > now;

            return new CrmUserSubscriptionSummaryDto
            {
                HasActiveSubscription = hasActive,
                Status = record.Status,
                Package = record.Package,
                ValidUntil = record.ValidUntil,
                Amount = record.Amount,
                IsTrial = record.IsTrial,
                Cancelled = record.Cancelled,
                CancelledDate = record.CancelledDate,
                SubscriptionId = record.SubscriptionID,
            };
        }
    }
}
