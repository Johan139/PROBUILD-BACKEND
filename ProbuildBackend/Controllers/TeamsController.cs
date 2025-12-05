using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Helpers;
using ProbuildBackend.Interface;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using System.Security.Claims;
using System.Text;
using IEmailSender = ProbuildBackend.Interface.IEmailSender;
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
        public readonly IEmailTemplateService _emailTemplate;
        public TeamsController(
            ApplicationDbContext context,
            UserManager<UserModel> userManager,
            IEmailSender emailSender,
            IDataProtectionProvider dataProtectionProvider,
            IHubContext<NotificationHub> hubContext, IEmailTemplateService emailTemplate)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
            _dataProtectionProvider = dataProtectionProvider;
            _hubContext = hubContext;
            _emailTemplate = emailTemplate;
        }

        [HttpPost("members")]
        public async Task<IActionResult> InviteMember([FromBody] InviteTeamMemberDto dto)
        {
            var currentUserId = User.FindFirstValue("UserId");
            if (currentUserId == null)
            {
                return Unauthorized();
            }

            var currentUserAsTeamMember = await _context.TeamMembers.FirstOrDefaultAsync(tm => tm.Id == currentUserId);
            var inviterId = currentUserAsTeamMember?.InviterId ?? currentUserId;

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

            var protector = _dataProtectionProvider.CreateProtector("TeamMemberInvitation");
            var raw = protector.Protect(dto.Email);
            var safeToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(raw));


            if (existingTeamMember != null)
            {
                if (existingTeamMember.Status == "Deleted")
                {
                    existingTeamMember.Status = "Invited";
                    existingTeamMember.FirstName = dto.FirstName;
                    existingTeamMember.LastName = dto.LastName;
                    existingTeamMember.Role = dto.Role;
                    teamMemberToInvite = existingTeamMember;
                    teamMemberToInvite.InvitationToken = safeToken;
                    teamMemberToInvite.TokenExpiration = DateTime.UtcNow.AddDays(7);
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
                    Status = "Invited",
                    InvitationToken = safeToken,
                    TokenExpiration = DateTime.UtcNow.AddDays(7)
                };
                emailSubject = "You have been invited to join a team on Probuild";
                _context.TeamMembers.Add(teamMemberToInvite);
            }

            await AssignDefaultPermissions(teamMemberToInvite);


            var notification = new NotificationModel
            {
                SenderId = inviterId,
                Message = $"You have been invited to a team by {inviterFullName}.",
                Timestamp = DateTime.UtcNow,
                Recipients = new List<string> { teamMemberToInvite.Id }
            };
            _context.Notifications.Add(notification);

            await _context.SaveChangesAsync();

            var existingUserAccount = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

            var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
            string callbackUrl;

            if (existingUserAccount != null)
            {
                // User already has a ProBuild account ? send them to accept-invite page
                callbackUrl = $"{frontendUrl}/accept-invite?token={safeToken}";
            }
            else
            {
                // User does NOT exist ? send them to team-member registration page
                callbackUrl = $"{frontendUrl}/register?token={safeToken}";
            }

            var TeamInvitationEmail = await _emailTemplate.GetTemplateAsync("TeamInvitationEmail");

            TeamInvitationEmail.Subject = TeamInvitationEmail.Subject.Replace("{{inviterFullName}}", inviterFullName);

            TeamInvitationEmail.Body = TeamInvitationEmail.Body.Replace("{{inviterFullName}}", inviterFullName)
                .Replace("{{InvitationLink}}", callbackUrl).Replace("{{Header}}", TeamInvitationEmail.HeaderHtml)
                .Replace("{{Footer}}", TeamInvitationEmail.FooterHtml);
            try
            {
                await _emailSender.SendEmailAsync(TeamInvitationEmail, dto.Email);
                Console.WriteLine($"Invitation email sent to {dto.Email}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending invitation email to {dto.Email}: {ex.Message}");
                return StatusCode(500, "Failed to send invitation email.");
            }



            await _hubContext.Clients.User(teamMemberToInvite.Id).SendAsync("ReceiveNotification", notification);

            return Ok(teamMemberToInvite);
        }

        [HttpGet("members")]
        public async Task<IActionResult> GetTeamMembers()
        {
            var currentUserId = User.FindFirstValue("UserId");
            if (currentUserId == null)
            {
                return Unauthorized();
            }

            var currentUserAsTeamMember = await _context.TeamMembers.FirstOrDefaultAsync(tm => tm.Id == currentUserId);
            var inviterIdToUse = currentUserAsTeamMember?.InviterId ?? currentUserId;

            var teamMembers = await _context.TeamMembers
                .Where(tm => tm.InviterId == inviterIdToUse)
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

        [HttpGet("members/profile/{id}")]
        public async Task<IActionResult> GetTeamMemberProfile(string id)
        {
            var teamMember = await _context.TeamMembers.FindAsync(id);
            if (teamMember == null)
            {
                return NotFound();
            }

            var userProfile = new UserModel
            {
                Id = teamMember.Id,
                FirstName = teamMember.FirstName,
                LastName = teamMember.LastName,
                Email = teamMember.Email,
                PhoneNumber = teamMember.PhoneNumber,
                UserType = teamMember.Role
            };

            return Ok(userProfile);
        }

        [HttpPatch("members/{id}/deactivate")]
        public async Task<IActionResult> DeactivateMember(string id)
        {
            var inviterId = User.FindFirstValue("UserId");
            var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(m => m.Id == id && m.InviterId == inviterId);

            if (teamMember == null)
            {
                return NotFound();
            }

            teamMember.Status = "Deactivated";
            await _context.SaveChangesAsync();

            var TeamDeactivateEmail = await _emailTemplate.GetTemplateAsync("AccountDeactivatedEmail");

            TeamDeactivateEmail.Body = TeamDeactivateEmail.Body.Replace("{{UserName}}", teamMember.FirstName + " " + teamMember.LastName).Replace("{{Header}}", TeamDeactivateEmail.HeaderHtml)
                .Replace("{{Footer}}", TeamDeactivateEmail.FooterHtml);

            await _emailSender.SendEmailAsync(TeamDeactivateEmail, teamMember.Email);

            return NoContent();
        }

        [HttpPatch("members/{id}/reactivate")]
        [Authorize]
        public async Task<IActionResult> ReactivateTeamMember(string id)
        {
            // 1. Get the current user's ID
            var inviterId = User.FindFirstValue("UserId");
            var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(m => m.Id == id && m.InviterId == inviterId);

            if (teamMember == null)
            {
                return NotFound("Team member not found.");
            }

            // 4. Update the team member's status
            teamMember.Status = "Registered";
            _context.TeamMembers.Update(teamMember);
            await _context.SaveChangesAsync();


            var TeamReactivateEmail = await _emailTemplate.GetTemplateAsync("AccountReactivatedEmail");

            var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
            var callbackUrl = $"{frontendUrl}/login";


            TeamReactivateEmail.Body = TeamReactivateEmail.Body.Replace("{{UserName}}", teamMember.FirstName + " " + teamMember.LastName)
                                                                             .Replace("{{LoginLink}}", callbackUrl).Replace("{{Header}}", TeamReactivateEmail.HeaderHtml)
                .Replace("{{Footer}}", TeamReactivateEmail.FooterHtml);
            // 5. Send a notification email
            await _emailSender.SendEmailAsync(TeamReactivateEmail, teamMember.Email);

            return Ok();
        }

        [HttpDelete("members/{id}")]
        public async Task<IActionResult> DeleteMember(string id)
        {
            var inviterId = User.FindFirstValue("UserId");
            var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(m => m.Id == id && m.InviterId == inviterId);

            if (teamMember == null)
            {
                return NotFound();
            }

            teamMember.Status = "Deleted";
            await _context.SaveChangesAsync();


            var TeamDeleteEmail = await _emailTemplate.GetTemplateAsync("AccountRemovedFromTeamEmail");

            var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
            var callbackUrl = $"{frontendUrl}/login";


            TeamDeleteEmail.Body = TeamDeleteEmail.Body.Replace("{{UserName}}", teamMember.FirstName + " " + teamMember.LastName).Replace("{{Header}}", TeamDeleteEmail.HeaderHtml)
                .Replace("{{Footer}}", TeamDeleteEmail.FooterHtml);

            await _emailSender.SendEmailAsync(TeamDeleteEmail, teamMember.Email);

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
                    // No default permissions
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
        [HttpPost("accept-invitation")]
        public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInviteDto dto)
        {
            var teamMember = await _context.TeamMembers
                .FirstOrDefaultAsync(tm => tm.InvitationToken == dto.Token &&
                                           tm.TokenExpiration > DateTime.UtcNow);

            if (teamMember == null)
                return BadRequest("Invalid or expired token.");

            teamMember.Status = "Registered";
            teamMember.InvitationToken = null;
            teamMember.TokenExpiration = null;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Invitation accepted." });
        }

        public class AcceptInviteDto
        {
            public string Token { get; set; }
        }
    }
}
