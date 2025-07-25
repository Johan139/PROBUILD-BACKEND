using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using ProbuildBackend.Middleware;
using ProbuildBackend.Helpers;

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
        private readonly IHubContext<NotificationHub> _hubContext;

        public TeamsController(
            ApplicationDbContext context,
            UserManager<UserModel> userManager,
            IEmailSender emailSender,
            IDataProtectionProvider dataProtectionProvider,
            IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
            _dataProtectionProvider = dataProtectionProvider;
            _hubContext = hubContext;
        }

        [HttpPost("members")]
        public async Task<IActionResult> InviteMember([FromBody] InviteTeamMemberDto dto)
        {
            var inviterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (inviterId == null)
            {
                return Unauthorized();
            }

            var inviter = await _userManager.FindByIdAsync(inviterId);
            if (inviter == null)
            {
                return Unauthorized(new { message = "Inviter not found." });
            }

            var existingTeamMember = await _context.TeamMembers.FirstOrDefaultAsync(tm => tm.Email == dto.Email && tm.InviterId == inviterId);
            var inviterFullName = $"{inviter.FirstName} {inviter.LastName}";
            TeamMember teamMemberToInvite;
            string emailSubject;
            bool isReInvite = false;

            if (existingTeamMember != null)
            {
                if (existingTeamMember.Status == "Deleted")
                {
                    existingTeamMember.Status = "Invited";
                    existingTeamMember.FirstName = dto.FirstName;
                    existingTeamMember.LastName = dto.LastName;
                    existingTeamMember.Role = dto.Role;
                    teamMemberToInvite = existingTeamMember;
                    emailSubject = "You have been re-invited to join a team on Probuild";
                    _context.TeamMembers.Update(existingTeamMember);
                    isReInvite = true;
                }
                else
                {
                    if (existingTeamMember.Role != dto.Role)
                    {
                        return Conflict(new { message = "A team member with this email already exists with a different role.", existingRole = existingTeamMember.Role });
                    }
                    return Conflict(new { message = "You have already invited a team member with this email address." });
                }
            }
            else
            {
                teamMemberToInvite = new TeamMember
                {
                    Id = Guid.NewGuid().ToString(),
                    InviterId = inviterId,
                    FirstName = dto.FirstName,
                    LastName = dto.LastName,
                    Email = dto.Email,
                    Role = dto.Role,
                    Status = "Invited"
                };
                emailSubject = "You have been invited to join a team on Probuild";
                _context.TeamMembers.Add(teamMemberToInvite);
            }

            await AssignDefaultPermissions(teamMemberToInvite);

            var protector = _dataProtectionProvider.CreateProtector("TeamMemberInvitation");
            var token = protector.Protect(dto.Email);
            teamMemberToInvite.InvitationToken = token;
            teamMemberToInvite.TokenExpiration = DateTime.UtcNow.AddDays(7);

            var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
            var callbackUrl = $"{frontendUrl}/register?token={Uri.EscapeDataString(token)}";
            var emailMessage = $"You have been invited to join a team by {inviterFullName}. Please <a href='{callbackUrl}'>click here</a> to register.";

            await _emailSender.SendEmailAsync(dto.Email, emailSubject, emailMessage);

            var notification = new NotificationModel
            {
                SenderId = inviterId,
                Message = $"You have been invited to a team by {inviterFullName}.",
                Timestamp = DateTime.UtcNow,
                Recipients = new List<string> { teamMemberToInvite.Id }
            };
            _context.Notifications.Add(notification);

            await _context.SaveChangesAsync();

            await _hubContext.Clients.User(teamMemberToInvite.Id).SendAsync("ReceiveNotification", notification);

            return Ok(teamMemberToInvite);
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

        [HttpGet("members/user/{userId}")]
        public async Task<IActionResult> GetTeamMembersByUser(string userId)
        {
            var teamMembers = await _context.TeamMembers
                .Where(tm => tm.InviterId == userId)
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

        [HttpPatch("members/{id}/deactivate")]
        public async Task<IActionResult> DeactivateMember(string id)
        {
            var inviterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(m => m.Id == id && m.InviterId == inviterId);

            if (teamMember == null)
            {
                return NotFound();
            }

            teamMember.Status = "Deactivated";
            await _context.SaveChangesAsync();

            await _emailSender.SendEmailAsync(teamMember.Email, "Team Member account Deactivated", "Your Team Member account has been deactivated.");

            return NoContent();
        }

        [HttpPatch("members/{id}/reactivate")]
        [Authorize]
        public async Task<IActionResult> ReactivateTeamMember(string id)
        {
            // 1. Get the current user's ID
            var inviterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(m => m.Id == id && m.InviterId == inviterId);

            if (teamMember == null)
            {
                return NotFound("Team member not found.");
            }

            // 4. Update the team member's status
            teamMember.Status = "Registered";
            _context.TeamMembers.Update(teamMember);
            await _context.SaveChangesAsync();

            // 5. Send a notification email
            await _emailSender.SendEmailAsync(teamMember.Email, "Team Member Account Reactivated", "Your Team Member account has been reactivated.");

            return Ok();
        }

        [HttpDelete("members/{id}")]
        public async Task<IActionResult> DeleteMember(string id)
        {
            var inviterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(m => m.Id == id && m.InviterId == inviterId);

            if (teamMember == null)
            {
                return NotFound();
            }

            teamMember.Status = "Deleted";
            await _context.SaveChangesAsync();

            await _emailSender.SendEmailAsync(teamMember.Email, "Team Member account Deleted", "Your Team Member account has been deleted from the team.");

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

        [HttpGet("members/{teamMemberId}/permissions")]
        public async Task<IActionResult> GetTeamMemberPermissions(string teamMemberId)
        {
            var teamMember = await _context.TeamMembers
                .Include(t => t.TeamMemberPermissions)
                .ThenInclude(tp => tp.Permission)
                .FirstOrDefaultAsync(t => t.Id == teamMemberId);

            if (teamMember == null)
            {
                return NotFound();
            }

            var permissions = teamMember.TeamMemberPermissions.Select(tp => tp.Permission.PermissionName.ToCamelCase()).ToList();
            return Ok(permissions);
        }

        [HttpPut("members/{teamMemberId}/permissions")]
        public async Task<IActionResult> UpdateTeamMemberPermissions(string teamMemberId, [FromBody] UpdatePermissionsDto dto)
        {
            var teamMember = await _context.TeamMembers
                .Include(t => t.TeamMemberPermissions)
                .FirstOrDefaultAsync(t => t.Id == teamMemberId);

            if (teamMember == null)
            {
                return NotFound();
            }

            var allPermissions = await _context.Permissions.ToListAsync();
            var invalidPermissions = dto.Permissions.Except(allPermissions.Select(p => p.PermissionName.ToCamelCase())).ToList();
            if (invalidPermissions.Any())
            {
                return BadRequest(new { message = "Invalid permission names.", invalidPermissions });
            }

            teamMember.TeamMemberPermissions.Clear();

            foreach (var permissionName in dto.Permissions)
            {
                var permission = allPermissions.First(p => p.PermissionName.ToCamelCase() == permissionName);
                teamMember.TeamMemberPermissions.Add(new TeamMemberPermission
                {
                    PermissionId = permission.PermissionId
                });
            }

            await _context.SaveChangesAsync();

            return NoContent();
        }

        private async Task AssignDefaultPermissions(TeamMember teamMember)
        {
            var defaultPermissions = new List<string>();

            switch (teamMember.Role)
            {
                case "Project Manager":
                    defaultPermissions = await _context.Permissions.Select(p => p.PermissionName).ToListAsync();
                    break;
                case "General Superintendent":
                case "Assistant Superintendent":
                case "Superintendent":
                    defaultPermissions.AddRange(new[] {
                        "CreateJobTasks", "DeleteJobTasks", "EditJobTasks",
                        "CreateJobSubtasks", "DeleteJobSubtasks", "EditJobSubtasks",
                        "CreateSubtaskNotes", "ManageSubtaskNotes"
                    });
                    break;
                case "Foreman":
                    defaultPermissions.AddRange(new[] { "CreateSubtaskNotes", "ManageSubtaskNotes" });
                    break;
                case "Chief Estimator":
                    defaultPermissions.AddRange(new[] {
                        "CreateJobs"
                    });
                    break;
            }

            if (defaultPermissions.Any())
            {
                var permissions = await _context.Permissions
                    .Where(p => defaultPermissions.Contains(p.PermissionName))
                    .ToListAsync();

                foreach (var permission in permissions)
                {
                    teamMember.TeamMemberPermissions.Add(new TeamMemberPermission
                    {
                        PermissionId = permission.PermissionId
                    });
                }
            }
        }
    }
}
