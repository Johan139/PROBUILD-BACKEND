using ProbuildBackend.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Middleware;
using NetTopologySuite.Geometries;
using System.Text.Json;

namespace ProbuildBackend.Services
{
    public class JobNotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;
        private readonly IHubContext<NotificationHub> _hubContext;

        public JobNotificationService(ApplicationDbContext context, EmailService emailService, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _emailService = emailService;
            _hubContext = hubContext;
        }

        public async Task NotifyUsersAboutNewJob(JobModel job)
        {
            if (job.Status != "BIDDING")
            {
                return;
            }

            var usersToNotify = new List<UserModel>();
            var externalUsersToNotify = new List<JobNotificationRecipient>();

            if (job.BiddingType == "CONNECTIONS_ONLY")
            {
                var gcConnections = await _context.Connections
                    .Where(c => c.RequesterId == job.UserId || c.ReceiverId == job.UserId)
                    .Select(c => c.RequesterId == job.UserId ? c.ReceiverId : c.RequesterId)
                    .ToListAsync();

                usersToNotify = await _context.Users
                    .Where(u => u.NotificationEnabled && gcConnections.Contains(u.Id))
                    .ToListAsync();
            }
            else if (job.BiddingType == "PUBLIC")
            {
                // Existing users
                if (job.JobAddressId != null && job.JobAddress != null && job.JobAddress.Latitude.HasValue && job.JobAddress.Longitude.HasValue)
                {
                    var jobLocation = new Point((double)job.JobAddress.Longitude.Value, (double)job.JobAddress.Latitude.Value) { SRID = 4326 };
                    var radiusInMeters = 1609.34; // 1 mile in meters

                    usersToNotify = await _context.Users
                        .Where(u => u.NotificationEnabled)
                        .Join(
                            _context.UserAddress,
                            user => user.Id,
                            address => address.UserId,
                            (user, address) => new { User = user, Address = address }
                        )
                        .Where(x => x.Address.Location.IsWithinDistance(jobLocation, x.User.NotificationRadiusMiles * radiusInMeters))
                        .Select(x => x.User)
                        .ToListAsync();

                    externalUsersToNotify = await _context.JobNotificationRecipients
                        .Where(r => r.notification_enabled == true && r.notification_radius_miles.HasValue && r.location_geo.IsWithinDistance(jobLocation, (double)r.notification_radius_miles.Value * radiusInMeters))
                        .ToListAsync();
                }
                else
                {
                    // Fallback if job location is not available
                    usersToNotify = await _context.Users.Where(u => u.NotificationEnabled).ToListAsync();
                    externalUsersToNotify = await _context.JobNotificationRecipients
                        .Where(r => r.notification_enabled == true)
                        .ToListAsync();
                }
            }

            var requiredTrades = JsonSerializer.Deserialize<List<string>>(job.RequiredSubcontractorTypes ?? "[]");

            // Filter existing users
            var filteredUsers = usersToNotify
                .Where(u => !string.IsNullOrEmpty(u.Trade) && requiredTrades.Any(trade => u.Trade.Contains(trade)))
                .Where(u => DoesJobMatchPreferences(u, job))
                .ToList();

            // Filter external users
            var filteredExternalUsers = externalUsersToNotify
                .Where(r => !string.IsNullOrEmpty(r.subtypes) && requiredTrades.Any(trade => r.subtypes.Contains(trade)))
                .ToList();

            // Process existing users
            string templatePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "PROBUILD-BACKEND", "ProbuildBackend", "EmailTemplates", "NewJobNotification.html");
            string emailTemplate = await File.ReadAllTextAsync(templatePath);

            foreach (var user in filteredUsers)
            {
                string emailBody = emailTemplate
                    .Replace("{{UserName}}", user.FirstName)
                    .Replace("{{JobTitle}}", job.ProjectName)
                    .Replace("{{JobLocation}}", job.Address)
                    .Replace("{{JobDescription}}", "A new job is available that matches your skills.") // TODO:Could in future introduce detailed description in JobModel
                    .Replace("{{JobDetailsLink}}", $"https://app.probuildai.com/jobs/{job.Id}")
                    .Replace("{{UnsubscribeLink}}", $"https://app.probuildai.com/subscription/unsubscribe?email={user.Email}");

                await _emailService.SendEmailAsync(user.Email, "New Job Available", emailBody);

                var notification = new NotificationModel
                {
                    Message = $"A new job '{job.ProjectName}' is available in your area.",
                    Timestamp = DateTime.UtcNow,
                    JobId = job.Id,
                    SenderId = "system",
                    Recipients = new List<string> { user.Id }
                };

                _context.Notifications.Add(notification);
                await _hubContext.Clients.User(user.Id).SendAsync("ReceiveNotification", notification);
            }

            // Process external users
            var existingUserEmails = new HashSet<string>(filteredUsers.Select(u => u.Email));
            foreach (var externalUser in filteredExternalUsers)
            {
                if (!string.IsNullOrEmpty(externalUser.email) && !existingUserEmails.Contains(externalUser.email))
                {
                    string emailBody = emailTemplate
                        .Replace("{{UserName}}", externalUser.name)
                        .Replace("{{JobTitle}}", job.ProjectName)
                        .Replace("{{JobLocation}}", job.Address)
                        .Replace("{{JobDescription}}", "A new job is available that matches your skills.")
                        .Replace("{{JobDetailsLink}}", $"https://app.probuildai.com/register?email={externalUser.email}") // TODO: Link to registration with pre-filled email. Do something similar to the TeamMember flow where we read from the URL query string
                        .Replace("{{UnsubscribeLink}}", $"https://app.probuildai.com/subscription/unsubscribe?email={externalUser.email}");

                    await _emailService.SendEmailAsync(externalUser.email, "New Job Available", emailBody);
                    
                    externalUser.last_job_notification = DateTime.UtcNow;
                    externalUser.total_notifications_sent = (externalUser.total_notifications_sent ?? 0) + 1;
                    _context.JobNotificationRecipients.Update(externalUser);
                }
            }

            await _context.SaveChangesAsync();
        }

        private bool DoesJobMatchPreferences(UserModel user, JobModel job)
        {
            if (string.IsNullOrEmpty(user.JobPreferences))
            {
                return true; // No preferences set, so all jobs match
            }

            try
            {
                var preferences = JsonSerializer.Deserialize<JobPreferences>(user.JobPreferences);

                if (preferences.Size != null && preferences.Size.Any() && !preferences.Size.Contains(job.BuildingSize.ToString())) // TODO: Assuming BuildingSize is a string for comparison here, need to check actual type
                {
                    return false;
                }

                if (preferences.Type != null && preferences.Type.Any() && !preferences.Type.Contains(job.JobType))
                {
                    return false;
                }

                return true;
            }
            catch (JsonException)
            {
                // TODO: Log the error 
                return true; // Or false, depending on what we want to do for invalid JSON
            }
        }
    }

    public class JobPreferences
    {
        public List<string> Size { get; set; }
        public List<string> Type { get; set; }
    }
}