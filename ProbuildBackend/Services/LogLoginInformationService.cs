using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;

namespace ProbuildBackend.Services
{
    public class LogLoginInformationService : ILogLoginInformationService
    {
        public ApplicationDbContext _context;

        public LogLoginInformationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogLoginAsync(
            Guid userId,
            string ip,
            string userAgent,
            bool success,
            string metadata = null,
            int keep = 5
        )
        {
            try
            {
                var audit = new UserLoginAudit
                {
                    UserId = userId,
                    LoginTime = DateTime.UtcNow,
                    IpAddress = ip,
                    UserAgent = userAgent?.Length > 500 ? userAgent.Substring(0, 500) : userAgent,
                    IsSuccess = success,
                    Metadata = metadata,
                };

                // single transaction for insert + prune
                _context.UserLoginAudit.Add(audit);
                await _context.SaveChangesAsync();

                // remove older than the most recent `keep` entries
                var older = await _context
                    .UserLoginAudit.Where(x => x.UserId == userId)
                    .OrderByDescending(x => x.LoginTime)
                    .Skip(keep) // keep first `keep`
                    .ToListAsync();

                if (older.Any())
                {
                    _context.UserLoginAudit.RemoveRange(older);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }
}
