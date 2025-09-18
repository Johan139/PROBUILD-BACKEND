using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Probuild.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class InvitationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<UserModel> _userManager;
        private readonly IEmailSender _emailSender;

        public InvitationController(ApplicationDbContext context, UserManager<UserModel> userManager, IEmailSender emailSender)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        [HttpPost("invite")]
        public async Task<IActionResult> InviteUser([FromBody] InvitationDto invitationDto)
        {
            var inviterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(inviterId))
            {
                return Unauthorized();
            }

            var inviter = await _userManager.FindByIdAsync(inviterId);
            if (inviter == null)
            {
                return Unauthorized();
            }

            var existingUser = await _userManager.FindByEmailAsync(invitationDto.Email);
            if (existingUser != null)
            {
                return BadRequest("A user with this email already exists.");
            }

            var existingInvitation = await _context.Invitations
                .FirstOrDefaultAsync(i => i.InviteeEmail == invitationDto.Email && i.ExpiresAt > DateTime.UtcNow);

            if (existingInvitation != null)
            {
                return BadRequest("An invitation has already been sent to this email address.");
            }

            var token = Guid.NewGuid().ToString();
            var invitation = new Invitation
            {
                InviterId = inviterId,
                InviteeEmail = invitationDto.Email,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            _context.Invitations.Add(invitation);
            await _context.SaveChangesAsync();

            var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
            var registrationLink = $"{frontendUrl}/register?invitationToken={token}";

            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "EmailTemplates", "InvitationEmail.html");
            var emailTemplate = await System.IO.File.ReadAllTextAsync(templatePath);

            emailTemplate = emailTemplate.Replace("{{firstName}}", invitationDto.FirstName);
            emailTemplate = emailTemplate.Replace("{{lastName}}", invitationDto.LastName);
            emailTemplate = emailTemplate.Replace("{{inviterFirstName}}", inviter.FirstName);
            emailTemplate = emailTemplate.Replace("{{inviterLastName}}", inviter.LastName);
            emailTemplate = emailTemplate.Replace("{{message}}", invitationDto.Message);
            emailTemplate = emailTemplate.Replace("{{invitationLink}}", registrationLink);

            await _emailSender.SendEmailAsync(invitationDto.Email, "You have been invited to join ProBuild", emailTemplate);

            return Ok(new { message = "Invitation sent successfully." });
        }

        [HttpGet("invitation/{token}")]
        public async Task<IActionResult> GetInvitation(string token)
        {
            var invitation = await _context.Invitations
                .FirstOrDefaultAsync(i => i.Token == token && i.ExpiresAt > DateTime.UtcNow);

            if (invitation == null)
            {
                return BadRequest("Invalid or expired invitation token.");
            }

            return Ok(new { email = invitation.InviteeEmail });
        }

        [HttpPost("register/invited")]
        public async Task<IActionResult> RegisterInvited([FromBody] InvitedRegistrationDto dto)
        {
            var invitation = await _context.Invitations
                .FirstOrDefaultAsync(i => i.Token == dto.Token && i.ExpiresAt > DateTime.UtcNow);

            if (invitation == null)
            {
                return BadRequest("Invalid or expired invitation token.");
            }

            var user = new UserModel
            {
                UserName = invitation.InviteeEmail,
                Email = invitation.InviteeEmail,
                PhoneNumber = dto.PhoneNumber
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