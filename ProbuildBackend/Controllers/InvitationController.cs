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

            var registrationLink = Url.Action("Register", "Account", new { invitationToken = token }, Request.Scheme);
            await _emailSender.SendEmailAsync(invitationDto.Email, "You have been invited to join ProBuild",
                $"Please register by clicking on this link: <a href='{registrationLink}'>Join Now</a>");

            return Ok(new { message = "Invitation sent successfully." });
        }
    }
}