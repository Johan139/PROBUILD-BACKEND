using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.DataProtection;
using System;

namespace ProbuildBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class TeamsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<UserModel> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IDataProtectionProvider _dataProtectionProvider;

        public TeamsController(ApplicationDbContext context, UserManager<UserModel> userManager, IEmailSender emailSender, IDataProtectionProvider dataProtectionProvider)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
            _dataProtectionProvider = dataProtectionProvider;
        }

        [HttpPost("members")]
        public async Task<IActionResult> InviteMember([FromBody] InviteTeamMemberDto dto)
        {
            var inviterId = User.FindFirstValue("UserId"); // Corrected claim type
            if (inviterId == null)
            {
                return Unauthorized();
            }

            // Check if a full user exists with this email
            var existingUser = await _userManager.FindByEmailAsync(dto.Email);
            if (existingUser != null && existingUser.UserType != dto.Role)
            {
                return Conflict(new { message = "A user with this email already exists with a different role.", existingRole = existingUser.UserType });
            }

            // Check if this inviter has already invited this email
            var existingInvitation = await _context.TeamMembers
                .FirstOrDefaultAsync(tm => tm.InviterId == inviterId && tm.Email == dto.Email);

            if (existingInvitation != null)
            {
                return Conflict(new { message = "You have already invited a team member with this email address." });
            }

            // Check if the team member has already registered under a different inviter
            var registeredMember = await _context.TeamMembers
                .FirstOrDefaultAsync(tm => tm.Email == dto.Email && tm.Status == "Registered");

            var newTeamMember = new TeamMember
            {
                Id = Guid.NewGuid().ToString(),
                InviterId = inviterId,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                Role = dto.Role,
            };

            if (registeredMember != null)
            {
                // The user is already registered, so just add them to the new team
                newTeamMember.PasswordHash = registeredMember.PasswordHash;
                newTeamMember.Status = "Registered";
                // TODO: Send a notification to the user that they've been added to a new team
            }
            else
            {
                // New user, send an invitation email
                var protector = _dataProtectionProvider.CreateProtector("TeamMemberInvitation");
                var token = protector.Protect(dto.Email);
                newTeamMember.InvitationToken = token;
                newTeamMember.TokenExpiration = DateTime.UtcNow.AddDays(7);

                var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
                var callbackUrl = $"{frontendUrl}/register?token={Uri.EscapeDataString(token)}";

                await _emailSender.SendEmailAsync(dto.Email, "You have been invited to join a team on Probuild",
                    $"You have been invited to join a team. Please <a href='{callbackUrl}'>click here</a> to register.");
            }

            _context.TeamMembers.Add(newTeamMember);
            await _context.SaveChangesAsync();

            return Ok(newTeamMember);
        }

        [HttpGet("members")]
        public async Task<IActionResult> GetTeamMembers()
        {
            var inviterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (inviterId == null)
            {
                return Unauthorized();
            }

            var teamMembers = await _context.TeamMembers
                .Where(tm => tm.InviterId == inviterId)
                .ToListAsync();

            return Ok(teamMembers);
        }

        [HttpGet("members/{id}")]
        public async Task<IActionResult> GetTeamMember(string id)
        {
            var teamMember = await _context.TeamMembers.FindAsync(id);
            if (teamMember == null)
            {
                return NotFound();
            }
            return Ok(teamMember);
        }
        
        [HttpDelete("members/{id}")]
        public async Task<IActionResult> RemoveMember(string id)
        {
            var inviterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (inviterId == null)
            {
                return Unauthorized();
            }

            var teamMember = await _context.TeamMembers
                .FirstOrDefaultAsync(tm => tm.Id == id && tm.InviterId == inviterId);

            if (teamMember == null)
            {
                return NotFound();
            }

            _context.TeamMembers.Remove(teamMember);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("my-teams")]
        public async Task<IActionResult> GetMyTeams()
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (userEmail == null)
            {
                return Unauthorized();
            }

            var teamMemberships = await _context.TeamMembers
                .Where(tm => tm.Email == userEmail)
                .Include(tm => tm.Inviter)
                .ToListAsync();

            var teams = teamMemberships.Select(tm => new TeamDto
            {
                Id = tm.Id,
                InviterId = tm.InviterId,
                InviterName = tm.Inviter.UserName
            }).ToList();

            return Ok(teams);
        }
    }
}
