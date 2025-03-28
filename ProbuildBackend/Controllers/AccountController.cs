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

        public AccountController(UserManager<UserModel> userManager, IEmailSender emailSender, IConfiguration configuration, ApplicationDbContext context)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _configuration = configuration;
            _context = context;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto model)
        {
            if (ModelState.IsValid)
            {
                var user = new UserModel
                {
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

                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);

                    Console.WriteLine($"Generated confirmation code for user {user.Id}: {code}");
                    var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL");
                    var callbackUrl = $"{frontendUrl}/confirm-email/?userId={user.Id}&code={Uri.EscapeDataString(code)}";

                    await _emailSender.SendEmailAsync(model.Email, "Confirm your email",
                        $"Please confirm this account for {user.UserName} by <a href='{callbackUrl}'>clicking here</a>.");

                    return Ok(new { message = "Registration successful, please verify your email." });
                }
                else
                {
                    return BadRequest(result.Errors);
                }
            }
            return BadRequest(ModelState);
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
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user != null && await _userManager.CheckPasswordAsync(user, model.Password) && user.EmailConfirmed == true)// add email comfirmation check
            {
                var token = GenerateJwtToken(user);
                return Ok(new { token, user.Id, user.FirstName, user.UserType });
            }

            return Unauthorized();
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
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordModel model)
        {
            if (string.IsNullOrEmpty(model.Email))
            {
                return BadRequest(new { message = "Email is required" });
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                return BadRequest(new { message = "User not found" });
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var callbackUrl = Url.Action(
                "ResetPassword",
                "Account",
                new { token = token, email = user.Email },
                protocol: HttpContext.Request.Scheme);

            // Send an email to the user with the reset link
            await _emailSender.SendEmailAsync(model.Email, "Reset Password",
                $"Please reset your password by using reset token : {token}");

            return Ok(new { message = "Password reset email sent." });
        }

        [HttpPost("resetpassword")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto model)
        {
            if (string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.Token) || string.IsNullOrEmpty(model.Password))
            {
                return BadRequest(new { message = "All fields are required." });
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                return BadRequest(new { message = "User not found" });
            }

            var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
            if (result.Succeeded)
            {
                return Ok(new { message = "Password reset successful." });
            }

            return BadRequest(new { message = "Error resetting password.", errors = result.Errors });
        }

    }
}

