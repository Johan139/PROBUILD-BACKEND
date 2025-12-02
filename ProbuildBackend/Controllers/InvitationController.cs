using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using IEmailSender = ProbuildBackend.Interface.IEmailSender;

namespace Probuild.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvitationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<UserModel> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<InvitationController> _logger;
        private readonly IEmailTemplateService _emailTemplate;

        public InvitationController(
            ApplicationDbContext context,
            UserManager<UserModel> userManager,
            IEmailSender emailSender,
            ILogger<InvitationController> logger,
            IEmailTemplateService emailTemplate
        )
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
            _emailTemplate = emailTemplate;
        }

        [HttpPost("invite")]
        public async Task<IActionResult> InviteUser([FromBody] InvitationDto invitationDto)
        {
            try
            {
                _logger.LogInformation("InviteUser endpoint hit.");
                var inviterId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(inviterId))
                {
                    return Unauthorized(new { message = "Inviter ID not found in token." });
                }

                var inviter = await _userManager.FindByIdAsync(inviterId);
                if (inviter == null)
                {
                    return Unauthorized(new { message = "Inviter not found." });
                }

                var existingUser = await _userManager.FindByEmailAsync(invitationDto.Email);
                if (existingUser != null)
                {
                    return BadRequest(new { message = "A user with this email already exists." });
                }

                if (!string.IsNullOrEmpty(invitationDto.PhoneNumber))
                {
                    var userByPhone = await _context.Users.FirstOrDefaultAsync(u =>
                        u.PhoneNumber == invitationDto.PhoneNumber
                    );
                    if (userByPhone != null)
                    {
                        return BadRequest(
                            new { message = "A user with this phone number already exists." }
                        );
                    }
                }

                var existingInvitation = await _context.Invitations.FirstOrDefaultAsync(i =>
                    i.InviteeEmail == invitationDto.Email && i.ExpiresAt > DateTime.UtcNow
                );

                if (existingInvitation != null)
                {
                    return BadRequest(
                        new
                        {
                            message = "An invitation has already been sent to this email address.",
                        }
                    );
                }

                var token = Guid.NewGuid().ToString();
                var invitation = new Invitation
                {
                    InviterId = inviterId,
                    InviteeEmail = invitationDto.Email,
                    Token = token,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    FirstName = invitationDto.FirstName,
                    LastName = invitationDto.LastName,
                };

                _context.Invitations.Add(invitation);
                _logger.LogInformation(
                    "Attempting to save invitation for {Email}",
                    invitationDto.Email
                );
                await _context.SaveChangesAsync();
                _logger.LogInformation(
                    "Invitation for {Email} saved successfully.",
                    invitationDto.Email
                );

                var frontendUrl =
                    Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
                var registrationLink = $"{frontendUrl}/register?invitationToken={token}";
                var InvitationEmail = await _emailTemplate.GetTemplateAsync(
                    "PlatformInvitationEmail"
                );

                //var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "EmailTemplates", "InvitationEmail.html");
                //var emailTemplate = await System.IO.File.ReadAllTextAsync(templatePath);

                InvitationEmail.Subject = InvitationEmail.Subject.Replace(
                    "{InviterName}",
                    invitationDto.FirstName + " " + invitationDto.LastName
                );
                InvitationEmail.Body = InvitationEmail
                    .Body.Replace(
                        "{InviterName}",
                        invitationDto.FirstName + " " + invitationDto.LastName
                    )
                    .Replace("{{InvitationLink}}", "")
                    .Replace("{{Header}}", InvitationEmail.HeaderHtml)
                    .Replace("{{Footer}}", InvitationEmail.FooterHtml);

                await _emailSender.SendEmailAsync(InvitationEmail, invitationDto.Email);

                return Ok(new { message = "Invitation sent successfully." });
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Error saving invitation to the database for {Email}.",
                    invitationDto.Email
                );
                return StatusCode(
                    500,
                    "An error occurred while saving the invitation. Please try again later."
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred in InviteUser for {Email}.",
                    invitationDto.Email
                );
                return StatusCode(500, "An unexpected error occurred. Please try again later.");
            }
        }

        [AllowAnonymous]
        [HttpGet("invitation/{token}")]
        public async Task<IActionResult> GetInvitation(string token)
        {
            var invitation = await _context.Invitations.FirstOrDefaultAsync(i =>
                i.Token == token && i.ExpiresAt > DateTime.UtcNow
            );

            if (invitation == null)
            {
                return BadRequest("Invalid or expired invitation token.");
            }

            return Ok(new { email = invitation.InviteeEmail });
        }

        [AllowAnonymous]
        [HttpPost("register/invited")]
        public async Task<IActionResult> RegisterInvited([FromBody] InvitedRegistrationDto dto)
        {
            var invitation = await _context.Invitations.FirstOrDefaultAsync(i =>
                i.Token == dto.Token && i.ExpiresAt > DateTime.UtcNow
            );

            if (invitation == null)
            {
                return BadRequest("Invalid or expired invitation token.");
            }

            var user = new UserModel
            {
                UserName = invitation.InviteeEmail,
                Email = invitation.InviteeEmail,
                PhoneNumber = dto.PhoneNumber,
            };

            var result = await _userManager.CreateAsync(user, dto.Password);

            if (!result.Succeeded)
            {
                return BadRequest(result.Errors);
            }

            invitation.IsAccepted = true;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Registration successful." });
        }
    }

    public class InvitedRegistrationDto
    {
        public string Token { get; set; }
        public string Password { get; set; }
        public string PhoneNumber { get; set; }
    }
}
