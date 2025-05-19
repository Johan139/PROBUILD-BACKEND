using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Elastic.Apm.Api;
using System.Web;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using System.Net;
using Google.Apis.Auth.OAuth2.Requests;
using Microsoft.AspNetCore.DataProtection;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountController : ControllerBase
    {
        private readonly UserManager<UserModel> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        private readonly IServiceProvider _serviceProvider;
        private readonly IDataProtectionProvider _dataProtectionProvider;

        public AccountController(UserManager<UserModel> userManager, IDataProtectionProvider dataProtectionProvider, IEmailSender emailSender, IConfiguration configuration, ApplicationDbContext context,
    IServiceProvider serviceProvider)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _configuration = configuration;
            _context = context;
            _serviceProvider = serviceProvider; // Initialize the field
            _dataProtectionProvider = dataProtectionProvider;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto model)
        {
            try
            {


            if (ModelState.IsValid)
            {
                var user = new UserModel
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = model.Email,
                    Email = model.Email,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    PhoneNumber = model.PhoneNumber,
                    CompanyName = model.CompanyName,
                    CompanyRegNo = model.CompanyRegNo,
                    VatNo = model.VatNo,
                    UserType = model.UserType,
                    ConstructionType = model.ConstructionType,
                    NrEmployees = model.NrEmployees,
                    YearsOfOperation = model.YearsOfOperation,
                    CertificationStatus = model.CertificationStatus,
                    CertificationDocumentPath = model.CertificationDocumentPath,
                    Availability = model.Availability,
                    Trade = model.Trade,
                    ProductsOffered = model.ProductsOffered,
                    SupplierType = model.SupplierType,
                    ProjectPreferences = model.ProjectPreferences,
                    DeliveryArea = model.DeliveryArea,
                    DeliveryTime = model.DeliveryTime,
                    Country = model.Country,
                    State = model.State,
                    City = model.City,
                    SubscriptionPackage = model.SubscriptionPackage
                };

                    var userAgree = new UserTermsAgreementModel
                    {
                        UserId = user.Id,
                        DateAgreed = DateTime.UtcNow
                    };
                    _context.UserTermsAgreement.Add(userAgree);
                    _context.SaveChanges();

                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);

                    Console.WriteLine($"Generated confirmation code for user {user.Id}: {code}");
                    var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL");
                    var callbackUrl = $"{frontendUrl}/confirm-email/?userId={user.Id}&code={Uri.EscapeDataString(code)}";

                    await _emailSender.SendEmailAsync(model.Email, "Confirm your email",
                        $"Please confirm this account for {user.UserName} by <a href='{callbackUrl}'>clicking here</a>.");

                        return Ok(new
                        {
                            message = "Registration successful, please verify your email.",
                            userId = user.Id
                        });
                    }
                else
                {
                    return BadRequest(result.Errors);
                }
            }
            return BadRequest(ModelState);
            }
            catch (Exception ex)
            {

                throw;
            }
        }

        [HttpGet("has-active-subscription/{userId}")]
        public async Task<ActionResult> HasActiveSubscription(string userId)
        {
            var hasActive = await _context.PaymentRecords
                .AnyAsync(p => p.UserId == userId && p.Status == "Success" && p.ValidUntil > DateTime.Now);

            return Ok(new { hasActive });
        }

        // GET api/users/byrole/{userType}
        [HttpGet("byrole/{userType}")]
        public async Task<ActionResult<IEnumerable<UserModel>>> GetUsersByName(string userType)
        {
            if (string.IsNullOrWhiteSpace(userType))
            {
                return BadRequest("Role parameter cannot be null or empty.");
            }

            var users = await _context.Users
                .Where(u => u.UserType == userType) // Adjust based on your actual property name
                .ToListAsync();

            if (users == null || !users.Any())
            {
                return NotFound("No users found with the specified role.");
            }

            return Ok(users);
        }

        // GET api/users/byUserId/{UserId}
        [HttpGet("byUserId/{id}")]
        public async Task<ActionResult<IEnumerable<UserModel>>> GetUserById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest("Id parameter cannot be null or empty.");
            }

            var users = await _context.Users
                .Where(u => u.Id == id) // Adjust based on your actual property name
                .ToListAsync();

            if (users == null || !users.Any())
            {
                return NotFound("No users found with the specified id.");
            }

            return Ok(users);
        }

        [HttpGet("confirmemail")]
        public async Task<IActionResult> ConfirmEmail(string userId, string code)
        {
            if (userId == null || code == null)
            {
                return BadRequest("Invalid email confirmation request.");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return BadRequest("Unable to find the user.");
            }

            var result = await _userManager.ConfirmEmailAsync(user, code);
            if (result.Succeeded)
            {
                return Ok(new { message = "Email confirmed successfully." });
            }
            else
            {
                return BadRequest(result.Errors);
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto model)
        {
            try
            {

     
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user != null && await _userManager.CheckPasswordAsync(user, model.Password) && user.EmailConfirmed == true)// add email comfirmation check
            {
                var token = GenerateJwtToken(user);
                return Ok(new { token, user.Id, user.FirstName, user.UserType });
            }

            return Unauthorized();
            }
            catch (Exception ex)
            {

                throw;
            }
        }



        private string GenerateJwtToken(UserModel user)
        {
            var claims = new[]
            {
            new Claim(JwtRegisteredClaimNames.Sub, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };
            var JWTKEY = Environment.GetEnvironmentVariable("JWT_KEY") ?? _configuration["Jwt:Key"];
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JWTKEY));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddMinutes(30),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [HttpPost("forgotpassword")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordModel model)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Select(u => new UserModel
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    Email = u.Email,
                    SecurityStamp = u.SecurityStamp
                })
                .FirstOrDefaultAsync(u => u.Email == model.Email);



            var protector = _dataProtectionProvider.CreateProtector($"{user.Id}:Default:ResetPassword");
            var token = protector.Protect("ResetToken:" + Guid.NewGuid().ToString());
            var frontendBaseUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? _configuration["URL:FrontendBaseUrl"]; ;
            var callbackUrl = $"{frontendBaseUrl}/reset-password?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}";

            await _emailSender.SendEmailAsync(model.Email, "Reset Password",
                $"Please reset your password by <a href='{callbackUrl}'>clicking here</a>.");

            return Ok();
        }
        public class ResetPasswordDto
        {
            public string email { get; set; }
            public string Token { get; set; }
            public string Password { get; set; }
        }

        [HttpPost("resetpassword")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto model)
        {
            // Fetch the existing user with all properties to preserve current values
            var existingUser = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == model.email);

            if (existingUser == null)
                return BadRequest("User not found");

            // Create a new instance with selected properties and merge with existing values
            var user = new UserModel
            {
                Id = existingUser.Id,
                UserName = existingUser.UserName,
                Email = existingUser.Email,
                SecurityStamp = existingUser.SecurityStamp,
                PasswordHash = existingUser.PasswordHash, // Preserve existing hash before update
                PhoneNumber = existingUser.PhoneNumber ?? "",
                EmailConfirmed = existingUser.EmailConfirmed,
                PhoneNumberConfirmed = existingUser.PhoneNumberConfirmed,
                TwoFactorEnabled = existingUser.TwoFactorEnabled,
                LockoutEnd = existingUser.LockoutEnd,
                LockoutEnabled = existingUser.LockoutEnabled,
                AccessFailedCount = existingUser.AccessFailedCount,
                FirstName = existingUser.FirstName ?? "",
                LastName = existingUser.LastName ?? "",
                UserType = existingUser.UserType ?? "",
                CompanyName = existingUser.CompanyName ?? "",
                CompanyRegNo = existingUser.CompanyRegNo ?? "",
                VatNo = existingUser.VatNo ?? "",
                ConstructionType = existingUser.ConstructionType ?? "",
                NrEmployees = existingUser.NrEmployees ?? "0", // Default to "0" for string representation
                YearsOfOperation = existingUser.YearsOfOperation ?? "",
                CertificationStatus = existingUser.CertificationStatus ?? "",
                CertificationDocumentPath = existingUser.CertificationDocumentPath ?? "",
                Availability = existingUser.Availability ?? "",
                Trade = existingUser.Trade ?? "",
                SupplierType = existingUser.SupplierType ?? "",
                ProductsOffered = existingUser.ProductsOffered ?? "",
                ProjectPreferences = existingUser.ProjectPreferences ?? "",
                DeliveryArea = existingUser.DeliveryArea ?? "",
                DeliveryTime = existingUser.DeliveryTime ?? "",
                Country = existingUser.Country ?? "",
                State = existingUser.State ?? "",
                City = existingUser.City ?? "",
                SubscriptionPackage = existingUser.SubscriptionPackage ?? "",
                IsVerified = existingUser.IsVerified
            };

            var protector = _dataProtectionProvider.CreateProtector($"{user.Id}:Default:ResetPassword");
            string unprotectedToken;
            try
            {
                unprotectedToken = protector.Unprotect(model.Token);
                if (!unprotectedToken.StartsWith("ResetToken:"))
                    return BadRequest("Invalid token");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Token Validation Error: {ex.Message}");
                return BadRequest("Invalid token");
            }

            // Log the state before update
            Console.WriteLine($"Before Update - Availability: {user.Availability ?? "null"}");

            // Set Availability to empty string (optional, based on your intent)
            user.Availability = "";
            Console.WriteLine($"After Setting - Availability: {user.Availability}");

            // Manually update the password
            var hasher = new PasswordHasher<UserModel>();
            var newPasswordHash = hasher.HashPassword(user, model.Password);
            user.PasswordHash = newPasswordHash;

            // Attach and update only the changed properties
            _context.Users.Attach(user);
            _context.Entry(user).Property(u => u.PasswordHash).IsModified = true;
            _context.Entry(user).Property(u => u.Availability).IsModified = true;
            await _context.SaveChangesAsync();


            return Ok();
        }

    }
}

