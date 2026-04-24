using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using BuildigBackend.Helpers;
using BuildigBackend.Interface;
using BuildigBackend.Middleware;
using BuildigBackend.Models;
using BuildigBackend.Models.DTO;
using IEmailSender = BuildigBackend.Interface.IEmailSender;
using BuildigBackend.Services;
using BuildigBackend.Models.DTO;
using BuildigBackend.Models;

namespace BuildigBackend.Controllers
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
        private readonly AzureBlobService _azureBlobService;
        public readonly IEmailTemplateService _emailTemplate;

        public TeamsController(
            ApplicationDbContext context,
            UserManager<UserModel> userManager,
            IEmailSender emailSender,
            IDataProtectionProvider dataProtectionProvider,
            IHubContext<NotificationHub> hubContext,
            IEmailTemplateService emailTemplate,
            AzureBlobService azureBlobService
        )
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
            _dataProtectionProvider = dataProtectionProvider;
            _hubContext = hubContext;
            _emailTemplate = emailTemplate;
            _azureBlobService = azureBlobService;
        }

        [HttpPost("members")]
        public async Task<IActionResult> InviteMember([FromBody] InviteTeamMemberDto dto)
        {
            var currentUserId = User.FindFirstValue("UserId");
            if (currentUserId == null)
            {
                return Unauthorized();
            }

            var currentUserAsTeamMember = await _context.TeamMembers.FirstOrDefaultAsync(tm =>
                tm.Id == currentUserId
            );
            var inviterId = currentUserAsTeamMember?.InviterId ?? currentUserId;

            var inviter = await _userManager.FindByIdAsync(inviterId);
            if (inviter == null)
            {
                return Unauthorized(new { message = "Inviter not found." });
            }

            var existingTeamMember = await _context.TeamMembers.FirstOrDefaultAsync(tm =>
                tm.Email == dto.Email && tm.InviterId == inviterId
            );
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
                    existingTeamMember.PhoneNumber = dto.PhoneNumber;
                    existingTeamMember.Role = dto.Role;
                    existingTeamMember.HourlyRate = dto.HourlyRate;
                    existingTeamMember.YearsExperience = dto.YearsExperience;
                    existingTeamMember.Certifications = dto.Certifications;
                    existingTeamMember.Specialties =
                        dto.Specialties != null
                            ? string.Join(
                                ",",
                                dto.Specialties.Where(x => !string.IsNullOrWhiteSpace(x))
                            )
                            : null;
                    existingTeamMember.CertificationFilesJson =
                        dto.CertificationFiles != null
                            ? JsonSerializer.Serialize(dto.CertificationFiles)
                            : null;
                    teamMemberToInvite = existingTeamMember;
                    teamMemberToInvite.InvitationToken = safeToken;
                    teamMemberToInvite.TokenExpiration = DateTime.UtcNow.AddDays(7);
                    emailSubject = "You have been re-invited to join a team on Buildig";
                    _context.TeamMembers.Update(existingTeamMember);
                    isReInvite = true;
                }
                else
                {
                    if (existingTeamMember.Role != dto.Role)
                    {
                        return Conflict(
                            new
                            {
                                message = "A team member with this email already exists with a different role.",
                                existingRole = existingTeamMember.Role,
                            }
                        );
                    }
                    return Conflict(
                        new
                        {
                            message = "You have already invited a team member with this email address.",
                        }
                    );
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
                    PhoneNumber = dto.PhoneNumber,
                    Role = dto.Role,
                    HourlyRate = dto.HourlyRate,
                    YearsExperience = dto.YearsExperience,
                    Certifications = dto.Certifications,
                    Specialties =
                        dto.Specialties != null
                            ? string.Join(
                                ",",
                                dto.Specialties.Where(x => !string.IsNullOrWhiteSpace(x))
                            )
                            : null,
                    CertificationFilesJson =
                        dto.CertificationFiles != null
                            ? JsonSerializer.Serialize(dto.CertificationFiles)
                            : null,
                    Status = "Invited",
                    InvitationToken = safeToken,
                    TokenExpiration = DateTime.UtcNow.AddDays(7),
                };
                emailSubject = "You have been invited to join a team on Buildig";
                _context.TeamMembers.Add(teamMemberToInvite);
            }

            await AssignDefaultPermissions(teamMemberToInvite);

            var notification = new NotificationModel
            {
                SenderId = inviterId,
                Message = $"You have been invited to a team by {inviterFullName}.",
                Timestamp = DateTime.UtcNow,
                Recipients = new List<string> { teamMemberToInvite.Id },
            };
            _context.Notifications.Add(notification);

            await _context.SaveChangesAsync();

            var existingUserAccount = await _context.Users.FirstOrDefaultAsync(u =>
                u.Email == dto.Email
            );

            var frontendUrl =
                Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
            string callbackUrl;

            if (existingUserAccount != null)
            {
                // User already has a BuildIG account ? send them to accept-invite page
                callbackUrl = $"{frontendUrl}/accept-invite?token={safeToken}";
            }
            else
            {
                // User does NOT exist ? send them to team-member registration page
                callbackUrl = $"{frontendUrl}/register?token={safeToken}";
            }

            var TeamInvitationEmail = await _emailTemplate.GetTemplateAsync("TeamInvitationEmail");

            TeamInvitationEmail.Subject = TeamInvitationEmail.Subject.Replace(
                "{{inviterFullName}}",
                inviterFullName
            );

            TeamInvitationEmail.Body = TeamInvitationEmail
                .Body.Replace("{{inviterFullName}}", inviterFullName)
                .Replace("{{InvitationLink}}", callbackUrl)
                .Replace("{{Header}}", TeamInvitationEmail.HeaderHtml)
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

            await _hubContext
                .Clients.User(teamMemberToInvite.Id)
                .SendAsync("ReceiveNotification", notification);

            return Ok(teamMemberToInvite);
        }

        [HttpPost("subcontractor-invite")]
        public async Task<IActionResult> SendSubcontractorInvite(
            [FromBody] SendSubcontractorInviteDto dto
        )
        {
            var currentUserId = User.FindFirstValue("UserId");
            if (currentUserId == null)
            {
                return Unauthorized();
            }

            var inviter = await _userManager.FindByIdAsync(currentUserId);
            if (inviter == null)
            {
                return Unauthorized(new { message = "Inviter not found." });
            }

            var inviterFullName = string.IsNullOrWhiteSpace(
                $"{inviter.FirstName} {inviter.LastName}".Trim()
            )
                ? inviter.Email
                : $"{inviter.FirstName} {inviter.LastName}".Trim();

            var frontendUrl =
                Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
            var callbackUrl = dto.JobId.HasValue
                ? $"{frontendUrl}/find-work?jobId={dto.JobId.Value}"
                : $"{frontendUrl}/find-work";

            string categoryLabel = string.IsNullOrWhiteSpace(dto.Category)
                ? "Trade"
                : dto.Category!;
            string tradeLabel = string.IsNullOrWhiteSpace(dto.TradeName)
                ? "Trade Package"
                : dto.TradeName!;
            string budgetLabel = dto.Budget.HasValue ? dto.Budget.Value.ToString("C0") : "TBD";
            string marketplaceLabel = dto.AlsoMarketplace ? "Yes" : "No";

            // TODO: Replace TeamInvitationEmail with a dedicated SubcontractorDirectInviteEmail template
            var template = await _emailTemplate.GetTemplateAsync("TeamInvitationEmail");
            template.Subject = $"Direct invite: {tradeLabel} on BuildIG";
            template.Body = template
                .Body.Replace("{{inviterFullName}}", inviterFullName)
                .Replace("{{InvitationLink}}", callbackUrl)
                .Replace("{{Header}}", template.HeaderHtml)
                .Replace("{{Footer}}", template.FooterHtml)
                .Replace(
                    "{{UserName}}",
                    string.IsNullOrWhiteSpace(dto.ContactName) ? dto.Email : dto.ContactName
                )
                .Replace("{{TradeName}}", tradeLabel)
                .Replace("{{Category}}", categoryLabel)
                .Replace("{{ScopeOfWork}}", dto.ScopeOfWork ?? "")
                .Replace("{{Budget}}", budgetLabel)
                .Replace("{{AlsoMarketplace}}", marketplaceLabel);

            try
            {
                await _emailSender.SendEmailAsync(template, dto.Email);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Error sending subcontractor invite email to {dto.Email}: {ex.Message}"
                );
                return StatusCode(500, "Failed to send subcontractor invite email.");
            }

            return Ok(new { message = "Subcontractor invite sent." });
        }

        [HttpGet("members")]
        public async Task<IActionResult> GetTeamMembers()
        {
            var currentUserId = User.FindFirstValue("UserId");
            if (currentUserId == null)
            {
                return Unauthorized();
            }

            var currentUserAsTeamMember = await _context.TeamMembers.FirstOrDefaultAsync(tm =>
                tm.Id == currentUserId
            );
            var inviterIdToUse = currentUserAsTeamMember?.InviterId ?? currentUserId;

            var teamMembers = await _context
                .TeamMembers.Where(tm => tm.InviterId == inviterIdToUse)
                .ToListAsync();

            return Ok(teamMembers);
        }

        [HttpGet("members/user/{userId}")]
        public async Task<IActionResult> GetTeamMembersByUser(string userId)
        {
            var teamMembers = await _context
                .TeamMembers.Where(tm => tm.InviterId == userId)
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

        [HttpPut("members/{id}")]
        public async Task<IActionResult> UpdateTeamMember(
            string id,
            [FromBody] UpdateTeamMemberDto dto
        )
        {
            var inviterId = User.FindFirstValue("UserId");
            if (inviterId == null)
            {
                return Unauthorized();
            }

            var currentUserAsTeamMember = await _context.TeamMembers.FirstOrDefaultAsync(tm =>
                tm.Id == inviterId
            );
            var inviterIdToUse = currentUserAsTeamMember?.InviterId ?? inviterId;

            var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(m =>
                m.Id == id && m.InviterId == inviterIdToUse
            );

            if (teamMember == null)
            {
                return NotFound("Team member not found.");
            }

            teamMember.Role = dto.Role;
            teamMember.HourlyRate = dto.HourlyRate;
            teamMember.YearsExperience = dto.YearsExperience;
            teamMember.Certifications = dto.Certifications;
            teamMember.Specialties =
                dto.Specialties != null
                    ? string.Join(",", dto.Specialties.Where(x => !string.IsNullOrWhiteSpace(x)))
                    : null;
            teamMember.CertificationFilesJson =
                dto.CertificationFiles != null
                    ? JsonSerializer.Serialize(dto.CertificationFiles)
                    : null;

            _context.TeamMembers.Update(teamMember);
            await _context.SaveChangesAsync();

            return Ok(teamMember);
        }

        [HttpPost("members/{id}/certifications/upload")]
        [RequestSizeLimit(200 * 1024 * 1024)]
        public async Task<IActionResult> UploadTeamMemberCertificationFiles(
            string id,
            [FromForm] UploadTeamMemberCertificationDto dto
        )
        {
            var inviterId = User.FindFirstValue("UserId");
            if (inviterId == null)
            {
                return Unauthorized();
            }

            var currentUserAsTeamMember = await _context.TeamMembers.FirstOrDefaultAsync(tm =>
                tm.Id == inviterId
            );
            var inviterIdToUse = currentUserAsTeamMember?.InviterId ?? inviterId;

            var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(m =>
                m.Id == id && m.InviterId == inviterIdToUse
            );

            if (teamMember == null)
            {
                return NotFound("Team member not found.");
            }

            if (dto.Files == null || !dto.Files.Any())
            {
                return BadRequest(new { error = "No certification files provided." });
            }

            var allowedExtensions = new[] { ".pdf", ".png", ".jpg", ".jpeg", ".doc", ".docx" };
            foreach (var file in dto.Files)
            {
                if (file.Length == 0)
                {
                    return BadRequest(new { error = $"Empty file detected: {file.FileName}" });
                }

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                {
                    return BadRequest(new { error = $"Invalid file type: {file.FileName}" });
                }
            }

            string connectionId =
                dto.ConnectionId
                ?? HttpContext?.Connection.Id
                ?? throw new InvalidOperationException("No valid connectionId provided.");

            var uploadedFileUrls = await _azureBlobService.UploadFiles(
                dto.Files,
                null,
                connectionId
            );

            var createdDocs = new List<TeamMemberCertificationDocument>();
            for (var index = 0; index < dto.Files.Count; index++)
            {
                var file = dto.Files[index];
                var url = uploadedFileUrls[index];
                var doc = new TeamMemberCertificationDocument
                {
                    TeamMemberId = teamMember.Id,
                    FileName = Path.GetFileName(new Uri(url).LocalPath),
                    BlobUrl = url,
                    ContentType = file.ContentType,
                    UploadedAt = DateTime.UtcNow,
                };

                _context.TeamMemberCertificationDocuments.Add(doc);
                createdDocs.Add(doc);
            }

            await _context.SaveChangesAsync();

            var responseFiles = createdDocs.Select(
                (doc, index) =>
                    new TeamMemberCertificationFileDto
                    {
                        Id = doc.Id,
                        Name = dto.Files[index].FileName,
                        Type = dto.Files[index].ContentType,
                        UploadedAt = doc.UploadedAt.ToString("yyyy-MM-dd"),
                        Url = doc.BlobUrl,
                    }
            );

            return Ok(new { fileUrls = uploadedFileUrls, files = responseFiles });
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
                UserType = teamMember.Role,
                Trade = teamMember.Specialties,
                CertificationDocumentPath = teamMember.CertificationFilesJson,
            };

            return Ok(userProfile);
        }

        [HttpPatch("members/{id}/deactivate")]
        public async Task<IActionResult> DeactivateMember(string id)
        {
            var inviterId = User.FindFirstValue("UserId");
            var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(m =>
                m.Id == id && m.InviterId == inviterId
            );

            if (teamMember == null)
            {
                return NotFound();
            }

            teamMember.Status = "Deactivated";
            await _context.SaveChangesAsync();

            var TeamDeactivateEmail = await _emailTemplate.GetTemplateAsync(
                "AccountDeactivatedEmail"
            );

            TeamDeactivateEmail.Body = TeamDeactivateEmail
                .Body.Replace("{{UserName}}", teamMember.FirstName + " " + teamMember.LastName)
                .Replace("{{Header}}", TeamDeactivateEmail.HeaderHtml)
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
            var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(m =>
                m.Id == id && m.InviterId == inviterId
            );

            if (teamMember == null)
            {
                return NotFound("Team member not found.");
            }

            // 4. Update the team member's status
            teamMember.Status = "Registered";
            _context.TeamMembers.Update(teamMember);
            await _context.SaveChangesAsync();

            var TeamReactivateEmail = await _emailTemplate.GetTemplateAsync(
                "AccountReactivatedEmail"
            );

            var frontendUrl =
                Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
            var callbackUrl = $"{frontendUrl}/login";

            TeamReactivateEmail.Body = TeamReactivateEmail
                .Body.Replace("{{UserName}}", teamMember.FirstName + " " + teamMember.LastName)
                .Replace("{{LoginLink}}", callbackUrl)
                .Replace("{{Header}}", TeamReactivateEmail.HeaderHtml)
                .Replace("{{Footer}}", TeamReactivateEmail.FooterHtml);
            // 5. Send a notification email
            await _emailSender.SendEmailAsync(TeamReactivateEmail, teamMember.Email);

            return Ok();
        }

        [HttpDelete("members/{id}")]
        public async Task<IActionResult> DeleteMember(string id)
        {
            var inviterId = User.FindFirstValue("UserId");
            var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(m =>
                m.Id == id && m.InviterId == inviterId
            );

            if (teamMember == null)
            {
                return NotFound();
            }

            teamMember.Status = "Deleted";
            await _context.SaveChangesAsync();

            var TeamDeleteEmail = await _emailTemplate.GetTemplateAsync(
                "AccountRemovedFromTeamEmail"
            );

            var frontendUrl =
                Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:4200";
            var callbackUrl = $"{frontendUrl}/login";

            TeamDeleteEmail.Body = TeamDeleteEmail
                .Body.Replace("{{UserName}}", teamMember.FirstName + " " + teamMember.LastName)
                .Replace("{{Header}}", TeamDeleteEmail.HeaderHtml)
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

            var teamMemberships = await _context
                .TeamMembers.Where(tm => tm.Email == userEmail)
                .Include(tm => tm.Inviter)
                .ToListAsync();

            var teams = teamMemberships
                .Select(tm => new TeamDto
                {
                    Id = tm.Id,
                    InviterId = tm.InviterId,
                    InviterName = tm.Inviter.UserName,
                })
                .ToList();

            return Ok(teams);
        }

        [HttpGet("members/{teamMemberId}/permissions")]
        public async Task<IActionResult> GetTeamMemberPermissions(string teamMemberId)
        {
            var teamMember = await _context
                .TeamMembers.Include(t => t.TeamMemberPermissions)
                    .ThenInclude(tp => tp.Permission)
                .FirstOrDefaultAsync(t => t.Id == teamMemberId);

            if (teamMember == null)
            {
                return NotFound();
            }

            var permissions = teamMember
                .TeamMemberPermissions.Select(tp => tp.Permission.PermissionName.ToCamelCase())
                .ToList();
            return Ok(permissions);
        }

        [HttpPut("members/{teamMemberId}/permissions")]
        public async Task<IActionResult> UpdateTeamMemberPermissions(
            string teamMemberId,
            [FromBody] UpdatePermissionsDto dto
        )
        {
            var teamMember = await _context
                .TeamMembers.Include(t => t.TeamMemberPermissions)
                .FirstOrDefaultAsync(t => t.Id == teamMemberId);

            if (teamMember == null)
            {
                return NotFound();
            }

            var allPermissions = await _context.Permissions.ToListAsync();
            var invalidPermissions = dto
                .Permissions.Except(allPermissions.Select(p => p.PermissionName.ToCamelCase()))
                .ToList();
            if (invalidPermissions.Any())
            {
                return BadRequest(
                    new { message = "Invalid permission names.", invalidPermissions }
                );
            }

            teamMember.TeamMemberPermissions.Clear();

            foreach (var permissionName in dto.Permissions)
            {
                var permission = allPermissions.First(p =>
                    p.PermissionName.ToCamelCase() == permissionName
                );
                teamMember.TeamMemberPermissions.Add(
                    new TeamMemberPermission { PermissionId = permission.PermissionId }
                );
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
                    defaultPermissions = await _context
                        .Permissions.Select(p => p.PermissionName)
                        .ToListAsync();
                    break;
                case "General Superintendent":
                case "Assistant Superintendent":
                case "Superintendent":
                    defaultPermissions.AddRange(
                        new[]
                        {
                            "CreateJobTasks",
                            "DeleteJobTasks",
                            "EditJobTasks",
                            "CreateJobSubtasks",
                            "DeleteJobSubtasks",
                            "EditJobSubtasks",
                            "CreateSubtaskNotes",
                            "ManageSubtaskNotes",
                        }
                    );
                    break;
                case "Foreman":
                    defaultPermissions.AddRange(
                        new[] { "CreateSubtaskNotes", "ManageSubtaskNotes" }
                    );
                    break;
                case "Chief Estimator":
                    // No default permissions
                    break;
            }

            if (defaultPermissions.Any())
            {
                var permissions = await _context
                    .Permissions.Where(p => defaultPermissions.Contains(p.PermissionName))
                    .ToListAsync();

                foreach (var permission in permissions)
                {
                    teamMember.TeamMemberPermissions.Add(
                        new TeamMemberPermission { PermissionId = permission.PermissionId }
                    );
                }
            }
        }

        [HttpPost("accept-invitation")]
        public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInviteDto dto)
        {
            var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(tm =>
                tm.InvitationToken == dto.Token && tm.TokenExpiration > DateTime.UtcNow
            );

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

