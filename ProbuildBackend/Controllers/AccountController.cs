using Google.Api.Ads.AdWords.v201809;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IEmailSender = ProbuildBackend.Interface.IEmailSender;

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
        public readonly IEmailTemplateService _emailTemplate;
        public AccountController(UserManager<UserModel> userManager, IDataProtectionProvider dataProtectionProvider, IEmailSender emailSender, IConfiguration configuration, ApplicationDbContext context,
    IServiceProvider serviceProvider, IEmailTemplateService emailTemplate)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _configuration = configuration;
            _context = context;
            _serviceProvider = serviceProvider;
            _dataProtectionProvider = dataProtectionProvider;
            _emailTemplate = emailTemplate;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                // Trim and normalize email
                var email = model.Email.Trim();
                var normalizedEmail = email.ToUpperInvariant();

                var user = new UserModel
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = email,
                    Email = email,
                    NormalizedUserName = normalizedEmail, // optional: handled automatically but safe
                    NormalizedEmail = normalizedEmail,    // optional: handled automatically but safe
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
                    JobPreferences = model.JobPreferences,
                    DeliveryArea = model.DeliveryArea,
                    DeliveryTime = model.DeliveryTime,
                    Country = model.Country,
                    State = model.State,
                    City = model.City,
                    SubscriptionPackage = model.SubscriptionPackage,
                    DateCreated = DateTime.UtcNow
                };

                var result = await _userManager.CreateAsync(user, model.Password);

                if (!result.Succeeded)
                    return BadRequest(result.Errors);

                var userMetaData = new UserMetaDataModel
                {
                    UserId = user.Id,
                    City = model.CityFromIP,
                    Country = model.CountryFromIP,
                    CreatedAt = DateTime.UtcNow,
                    IpAddress = model.IpAddress,
                    Latitude = model.LatitudeFromIP,
                    Longitude = model.LongitudeFromIP,
                    Region = model.RegionFromIP,
                    TimeZone = model.Timezone,
                    OperatingSystem = model.OperatingSystem
                };

                 _context.UserMetaData.Add(userMetaData);


                // Only save agreement if user was created successfully
                var userAgree = new UserTermsAgreementModel
                {
                    UserId = user.Id,
                    DateAgreed = DateTime.UtcNow
                };

                _context.UserTermsAgreement.Add(userAgree);
                await _context.SaveChangesAsync();

                var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? _configuration["FrontEnd:FRONTEND_URL"];
                var callbackUrl = $"{frontendUrl}/confirm-email/?userId={user.Id}&code={Uri.EscapeDataString(code)}";

                var EmailConfirmation = await _emailTemplate.GetTemplateAsync("ConfirmAccountEmail");
                EmailConfirmation.Body = EmailConfirmation.Body.Replace("{{ConfirmLink}}", callbackUrl).Replace("{{UserName}}", model.FirstName + " " + model.LastName).Replace("{{Header}}", EmailConfirmation.HeaderHtml).Replace("{{UserName}}", model.FirstName + " " + model.LastName)
                .Replace("{{Footer}}", EmailConfirmation.FooterHtml);
                await _emailSender.SendEmailAsync(EmailConfirmation, model.Email);

                return Ok(new
                {
                    message = "Registration successful, please verify your email.",
                    userId = user.Id
                });
            }
            catch (Exception ex)
            {
                // Optional: Log the exception before rethrowing
                Console.WriteLine("Error during registration: " + ex.Message);
                throw;
            }
        }


        [HttpGet("resend-email-verification/{email}")]

        public async Task<ActionResult> ResendEmailLink(string email)
        {

            var user = _context.Users.Where(p => p.Email == email).FirstOrDefault();
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? _configuration["FrontEnd:FRONTEND_URL"];
            var callbackUrl = $"{frontendUrl}/confirm-email/?userId={user.Id}&code={Uri.EscapeDataString(code)}";

            var EmailConfirmation = await _emailTemplate.GetTemplateAsync("ConfirmAccountEmail");
            EmailConfirmation.Body = EmailConfirmation.Body.Replace("{{ConfirmLink}}", callbackUrl).Replace("{{Header}}", EmailConfirmation.HeaderHtml)
                .Replace("{{Footer}}", EmailConfirmation.FooterHtml).Replace("{{UserName}}", user.FirstName + " " + user.LastName);

            await _emailSender.SendEmailAsync(EmailConfirmation,user.Email);

            return Ok(new
            {
                message = "Resend successful, please verify your email.",
                userId = user.Id
            });
        }

        [HttpGet("has-active-subscription/{userId}")]
        public async Task<ActionResult> HasActiveSubscription(string userId)
        {
            var hasActive = await _context.PaymentRecords.AnyAsync(p =>
                p.Status == "Active"
                && p.ValidUntil > DateTime.UtcNow
                && (p.UserId == userId || p.AssignedUser == userId)   // <- check either
            );
            return Ok(new { hasActive });
        }

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

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<UserSearchDto>>> SearchUsers([FromQuery] string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return BadRequest("Search term cannot be empty.");
            }

            var users = await _context.Users
                .Where(u => u.FirstName.Contains(term) || u.LastName.Contains(term) || u.CompanyName.Contains(term) || u.Trade.Contains(term) || u.Email.Contains(term) || u.PhoneNumber.Contains(term))
                .Select(u => new UserSearchDto
                {
                    Id = u.Id,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    UserType = u.UserType,
                    CompanyName = u.CompanyName,
                    Email = u.Email,
                    PhoneNumber = u.PhoneNumber,
                    ConstructionType = u.ConstructionType,
                    Trade = u.Trade,
                    SupplierType = u.SupplierType,
                    ProductsOffered = u.ProductsOffered,
                    Country = u.Country,
                    City = u.City
                })
                .ToListAsync();

            return Ok(users);
        }
        [HttpGet("users")]
        public async Task<ActionResult<IEnumerable<UserSearchDto>>> GetUsers()
        {
            var users = await _context.Users
                .Select(u => new UserSearchDto
                {
                    Id = u.Id,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    UserType = u.UserType,
                    CompanyName = u.CompanyName,
                    Email = u.Email,
                    PhoneNumber = u.PhoneNumber,
                    ConstructionType = u.ConstructionType,
                    Trade = u.Trade,
                    SupplierType = u.SupplierType,
                    ProductsOffered = u.ProductsOffered,
                    Country = u.Country,
                    City = u.City
                })
                .ToListAsync();

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
                    var refreshToken = GenerateRefreshToken();

                    var refreshTokenEntity = new RefreshToken
                    {
                        UserId = user.Id,
                        Token = refreshToken,
                        Expires = DateTime.UtcNow.AddDays(7),
                        Created = DateTime.UtcNow
                    };

                    _context.RefreshTokens.Add(refreshTokenEntity);
                    await _context.SaveChangesAsync();

                    return Ok(new
                    {
                        token,
                        refreshToken,
                        userId = user.Id,
                        firstName = user.FirstName,
                        lastName = user.LastName,
                        userType = user.UserType
                    });
                }
                if (user != null && !user.EmailConfirmed)
                {
                    return Unauthorized(new { error = "Email address has not been verified. Please check your inbox and spam folder." });
                }
                return Unauthorized(new { error = "Invalid login credentials. Please try again." });
            }
            catch (Exception ex)
            {

                throw;
            }
        }

        public record RefreshTokenRequest(string RefreshToken);

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            var refreshToken = request.RefreshToken;

            var storedToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

            if (storedToken == null || storedToken.Revoked != null || storedToken.Expires < DateTime.UtcNow)
            {
                return Unauthorized("Invalid refresh token.");
            }

            // Revoke the old refresh token immediately
            storedToken.Revoked = DateTime.UtcNow;

            string newAccessTokenString;

            // Case 1: Regular User
            var user = await _context.Users.FindAsync(storedToken.UserId);
            if (user != null)
            {
                newAccessTokenString = GenerateJwtToken(user);
            }
            else
            {
                var memberById = await _context.TeamMembers.FindAsync(storedToken.UserId);
                if (memberById == null)
                {
                    return Unauthorized("User or team member not found for the given token.");
                }

                var teamMembers = await _context.TeamMembers
                    .Where(tm => tm.Email == memberById.Email && tm.Status == "Registered")
                    .ToListAsync();

                if (!teamMembers.Any())
                {
                    return Unauthorized("Team member not found for the given token.");
                }

                var firstMember = teamMembers.First();
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Email, firstMember.Email),
                    new Claim("isTeamMember", "true"),
                };
                foreach (var member in teamMembers)
                {
                    claims.Add(new Claim("team", $"{member.Id}:{member.InviterId}"));
                }

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
                var newAccessToken = new JwtSecurityToken(
                    issuer: _configuration["Jwt:Issuer"],
                    audience: _configuration["Jwt:Audience"],
                    claims: claims,
                    expires: DateTime.UtcNow.AddMinutes(30),
                    signingCredentials: creds
                );
                newAccessTokenString = new JwtSecurityTokenHandler().WriteToken(newAccessToken);
            }

            // Generate a new refresh token for both cases
            var newRefreshToken = GenerateRefreshToken();
            var newRefreshTokenEntity = new RefreshToken
            {
                UserId = storedToken.UserId, // Re-use the same ID (either UserModel or TeamMember)
                Token = newRefreshToken,
                Expires = DateTime.UtcNow.AddDays(7),
                Created = DateTime.UtcNow
            };

            _context.RefreshTokens.Add(newRefreshTokenEntity);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                token = newAccessTokenString,
                refreshToken = newRefreshToken
            });
        }

        [HttpPost("trailversion")]
        public async Task<IActionResult> TrailVersionSubscription([FromBody] TrialRequestDTO dto)
        {
            try
            {
                var user = await _context.Users.FindAsync(dto.UserId);
                if (user == null) return NotFound("User not found.");

                var existingTrial = await _context.PaymentRecords
                    .AnyAsync(p => p.UserId == dto.UserId && p.IsTrial == true && p.Status == "Active");

                if (existingTrial)
                    return BadRequest("Trial already used.");

                var trial = new PaymentRecord
                {
                    UserId = dto.UserId,
                    Package = dto.PackageName,
                    StripeSessionId = "TRIAL-NO-SESSION",
                    Status = "Active",
                    PaidAt = DateTime.UtcNow,
                    ValidUntil = DateTime.UtcNow.AddDays(7),
                    Amount = 0,
                    IsTrial = true,
                    SubscriptionID = GenerateTrialSubscriptionId()
                };

                _context.PaymentRecords.Add(trial);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception)
            {

                throw;
            }
        }
        public static string GenerateTrialSubscriptionId()
        {
            // Generate 24 random bytes → longer output
            var bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            // Encode and clean
            var base64 = Convert.ToBase64String(bytes)
                .Replace("+", "")
                .Replace("/", "")
                .Replace("=", "");

            return $"trial_{base64}";
        }
        private string GenerateJwtToken(UserModel user)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Email ?? ""),
                new Claim(ClaimTypes.Email, user.Email), 
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("UserId", user.Id),
                new Claim("userId", user.Id), 
                new Claim(ClaimTypes.NameIdentifier, user.Id), 
                new Claim("UserType", user.UserType ?? ""),
                new Claim("FirstName", user.FirstName ?? ""),
                new Claim("LastName", user.LastName ?? ""),
                new Claim("CompanyName", user.CompanyName ?? "")
            };

            var JWTKEY = Environment.GetEnvironmentVariable("JWT_KEY") ?? _configuration["Jwt:Key"];
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JWTKEY));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(30),
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



            var protector = _dataProtectionProvider
     .CreateProtector($"{user.Id}:Default:ResetPassword")
     .ToTimeLimitedDataProtector();
            var token = protector.Protect("ResetToken:" + Guid.NewGuid(), lifetime: TimeSpan.FromMinutes(15));

            var frontendBaseUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? _configuration["FrontEnd:FRONTEND_URL"]; ;
            var callbackUrl = $"{frontendBaseUrl}/reset-password?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}";

            var ResetPassword = await _emailTemplate.GetTemplateAsync("PasswordResetEmail");

            ResetPassword.Body = ResetPassword.Body
                .Replace("{{ResetLink}}", callbackUrl)
                .Replace("{{UserName}}", $"{user.FirstName} {user.LastName}")
                .Replace("{{Header}}", ResetPassword.HeaderHtml)
                .Replace("{{Footer}}", ResetPassword.FooterHtml);



            await _emailSender.SendEmailAsync(ResetPassword,user.Email);

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
                JobPreferences = existingUser.JobPreferences ?? "",
                DeliveryArea = existingUser.DeliveryArea ?? "",
                DeliveryTime = existingUser.DeliveryTime ?? "",
                Country = existingUser.Country ?? "",
                State = existingUser.State ?? "",
                City = existingUser.City ?? "",
                SubscriptionPackage = existingUser.SubscriptionPackage ?? "",
                IsVerified = existingUser.IsVerified
            };

            var protector = _dataProtectionProvider
     .CreateProtector($"{user.Id}:Default:ResetPassword")
     .ToTimeLimitedDataProtector();
            string unprotectedToken;
            try
            {
                unprotectedToken = protector.Unprotect(model.Token);

                if (!unprotectedToken.StartsWith("ResetToken:"))
                    return BadRequest("Invalid token.");
            }
            catch (CryptographicException ex)
            {
                Console.WriteLine($"Token validation failed: {ex.Message}");
                return BadRequest(new { error = "Token has expired." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
                return StatusCode(500, new { error = "Unexpected error occurred." });
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

        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
                return Convert.ToBase64String(randomNumber);
            }
        }

        [HttpGet("invitation/{token}")]
        public async Task<IActionResult> GetInvitation(string token)
        {
            var protector = _dataProtectionProvider.CreateProtector("TeamMemberInvitation");
            string unprotectedToken;
            try
            {
                unprotectedToken = protector.Unprotect(token);
            }
            catch (Exception)
            {
                return BadRequest("Invalid invitation token.");
            }

            var teamMember = await _context.TeamMembers
                .FirstOrDefaultAsync(tm => tm.InvitationToken == token && tm.TokenExpiration > DateTime.UtcNow);

            if (teamMember == null)
            {
                return BadRequest("Invalid or expired invitation token.");
            }

            return Ok(new { teamMember.FirstName, teamMember.LastName, teamMember.Email, teamMember.Role });
        }

        [HttpPost("register/team-member")]
        public async Task<IActionResult> RegisterInvited([FromBody] InvitedRegistrationDto dto)
        {
            var protector = _dataProtectionProvider.CreateProtector("TeamMemberInvitation");
            string unprotectedToken;
            try
            {
                unprotectedToken = protector.Unprotect(dto.Token);
            }
            catch (Exception)
            {
                return BadRequest("Invalid invitation token.");
            }

            var teamMember = await _context.TeamMembers
                .FirstOrDefaultAsync(tm => tm.InvitationToken == dto.Token && tm.TokenExpiration > DateTime.UtcNow);

            if (teamMember == null)
            {
                return BadRequest("Invalid or expired invitation token.");
            }

            var hasher = new PasswordHasher<TeamMember>();
            teamMember.PasswordHash = hasher.HashPassword(teamMember, dto.Password);
            teamMember.PhoneNumber = dto.PhoneNumber;
            teamMember.Status = "Registered";
            teamMember.InvitationToken = null;
            teamMember.TokenExpiration = null;

            // Update all other pending invitations for this email address
            var otherInvitations = await _context.TeamMembers
                .Where(tm => tm.Email == teamMember.Email && tm.Status == "Invited")
                .ToListAsync();

            foreach (var invitation in otherInvitations)
            {
                invitation.PasswordHash = teamMember.PasswordHash;
                invitation.Status = "Registered";
                invitation.InvitationToken = null;
                invitation.TokenExpiration = null;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Registration successful." });
        }

        [HttpPost("login/member")]
       public async Task<IActionResult> LoginMember([FromBody] LoginDto model)
       {
           var teamMembers = await _context.TeamMembers
               .Where(tm => tm.Email == model.Email && tm.Status == "Registered")
               .ToListAsync();

           if (!teamMembers.Any())
           {
               return Unauthorized();
           }

           var firstMember = teamMembers.First();
           var hasher = new PasswordHasher<TeamMember>();
           var result = hasher.VerifyHashedPassword(firstMember, firstMember.PasswordHash, model.Password);

           if (result == PasswordVerificationResult.Failed)
           {
               return Unauthorized();
           }

           var claims = new List<Claim>
           {
               new Claim(ClaimTypes.Email, firstMember.Email),
               new Claim("isTeamMember", "true"),
           };

           foreach (var member in teamMembers)
           {
               claims.Add(new Claim("team", $"{member.Id}:{member.InviterId}"));
           }

           var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
           var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

           var token = new JwtSecurityToken(
               issuer: _configuration["Jwt:Issuer"],
               audience: _configuration["Jwt:Audience"],
               claims: claims,
               expires: DateTime.UtcNow.AddMinutes(30),
               signingCredentials: creds
           );

            var refreshToken = GenerateRefreshToken();
            var refreshTokenEntity = new RefreshToken
            {
                UserId = firstMember.Id, // Using TeamMember's Id
                Token = refreshToken,
                Expires = DateTime.UtcNow.AddDays(7),
                Created = DateTime.UtcNow
            };

            _context.RefreshTokens.Add(refreshTokenEntity);
            await _context.SaveChangesAsync();

           return Ok(new
           {
               token = new JwtSecurityTokenHandler().WriteToken(token),
               refreshToken = refreshToken
           });
       }

       [HttpPut("preferences")]
       public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesDto model)
       {
           var userId = User.FindFirstValue("UserId");
           if (string.IsNullOrEmpty(userId))
           {
               return Unauthorized();
           }

           var user = await _userManager.FindByIdAsync(userId);
           if (user == null)
           {
               return NotFound("User not found.");
           }

           user.NotificationRadiusMiles = model.NotificationRadiusMiles;
           user.JobPreferences = JsonConvert.SerializeObject(model.JobPreferences);

           var result = await _userManager.UpdateAsync(user);

           if (result.Succeeded)
           {
               return Ok(new { message = "Preferences updated successfully." });
           }

           return BadRequest(result.Errors);
       }

       [HttpGet("address")]
       public async Task<IActionResult> GetUserAddress()
       {
           var userId = User.FindFirstValue("UserId");
           if (string.IsNullOrEmpty(userId))
           {
               return Unauthorized();
           }

           var userAddress = await _context.UserAddress
               .Where(a => a.UserId == userId)
               .FirstOrDefaultAsync();

           if (userAddress == null)
           {
               return NotFound("Address not found.");
           }

           return Ok(userAddress);
       }
   
        // GET api/users/byUserId/{UserId}
        [HttpGet("countries")]
        public async Task<ActionResult<IEnumerable<UserModel>>> GetCountries()
        {
            try
            {

  
            var countries = await _context.Countries
                .ToListAsync();

            if (countries == null || !countries.Any())
            {
                return NotFound("No countries found.");
            }

            return Ok(countries);
            }
            catch (Exception ex)
            {

                throw;
            }
        }
        // GET api/users/byUserId/{UserId}
        [HttpGet("states")]
        public async Task<ActionResult<IEnumerable<UserModel>>> GetStates()
        {
            try
            {


                var state = await _context.States
                    .ToListAsync();

                if (state == null || !state.Any())
                {
                    return NotFound("No countries found.");
                }

                return Ok(state);
            }
            catch (Exception ex)
            {

                throw;
            }
        }
    }
}

