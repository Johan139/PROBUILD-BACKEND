using iText.Layout.Element;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Services
{
    public class ArchiveService : IArchiveService
    {
        private readonly ApplicationDbContext _context;

        public ArchiveService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<ArchivedItemDto>> GetArchivedItemsAsync([FromQuery] string userId)
        {
            try
            {


                var archivedJobs = await _context.Jobs
                    .Where(j => j.ArchivedAt != null && j.UserId == userId)
                    .Select(j => new ArchivedItemDto
                    {
                        Id = j.Id.ToString(),
                        Type = "JOB",
                        Title = j.ProjectName ?? "",
                        Status = j.Status ?? "",
                        ArchivedAt = j.ArchivedAt ?? DateTime.MinValue
                    })
                    .ToListAsync();

                var archivedQuotes =
                    await (
                        from q in _context.Quotes
                        join u in _context.Users
                            on q.SentTo equals u.Id into userJoin
                        from u in userJoin.DefaultIfEmpty()
                        where q.ArchivedAt != null && q.CreatedID == userId
                        select new ArchivedItemDto
                        {
                            Id = q.Id.ToString(),
                            Type = q.DocumentType,
                            Title = q.Number ?? "",
                            Status = q.Status ?? "",
                            ArchivedAt = q.ArchivedAt,

                            Client =
                                u == null
                                    ? ""
                                    : !string.IsNullOrWhiteSpace(u.FirstName) || !string.IsNullOrWhiteSpace(u.LastName)
                                        ? (u.FirstName + " " + u.LastName).Trim()
                                        : u.Email,

                            Amount =
                                _context.QuoteVersions
                                    .Where(v => v.QuoteId == q.Id)
                                    .OrderByDescending(v => v.Version) // or VersionNumber
                                    .Select(v => v.Total)
                                    .FirstOrDefault()
                        }
                    ).ToListAsync();

                var archivedDocuments = await _context.ProfileDocuments
    .Where(d => d.ArchivedAt != null && d.UserId == userId)
    .Select(d => new ArchivedItemDto
    {
        Id = d.Id.ToString(),
        Type = "DOCUMENT",
        Title = d.FileName,
        Project = "",
        DocumentType = "Profile",
        Size = 0,
        ArchivedAt = d.ArchivedAt.Value
    })
    .ToListAsync();



                var archivedItems = archivedJobs
         .Concat(archivedQuotes)
         .Concat(archivedDocuments)
         .OrderByDescending(x => x.ArchivedAt)
         .ToList();


                return archivedItems;
            }
            catch (Exception ex)
            {
                return new List<ArchivedItemDto> { };
            }
        }
        public async Task<bool> ArchiveJob(int jobid)
        {
            try
            {
                var Job = await _context.Jobs.Where(q => q.Id == jobid).FirstOrDefaultAsync();
                if (Job == null)
                    return false;

                Job.ArchivedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception)
            {
                return false;

            }
        }
        public async Task<bool> ArchiveQuoteOrInvoice(Guid id)
        {
            try
            {
                var quoteOrinvoice = await _context.Quotes.Where(q => q.Id == id).FirstOrDefaultAsync();
                if (quoteOrinvoice == null)
                    return false;

                quoteOrinvoice.ArchivedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception)
            {
                return false;
               
            }
        }
        public async Task<bool> UnarchiveAsync(string itemId, string itemType, string userId)
        {
            try
            {
                switch (itemType.ToUpper())
                {
                    case "JOB":
                        {
                            var job = await _context.Jobs
                                .FirstOrDefaultAsync(j => j.Id == Convert.ToInt32(itemId) && j.UserId == userId);

                            if (job == null) return false;

                            job.ArchivedAt = null;
                            break;
                        }

                    case "QUOTE":
                    case "INVOICE":
                        {
                            var quote = await _context.Quotes
                                .FirstOrDefaultAsync(q => q.Id == Guid.Parse(itemId) && q.CreatedID == userId);

                            if (quote == null) return false;

                            quote.ArchivedAt = null;
                            break;
                        }
                    case "DOCUMENT":
                        {
                            var profiledocument = await _context.ProfileDocuments
                             .FirstOrDefaultAsync(q => q.Id == Convert.ToInt32(itemId));

                            if (profiledocument == null) return false;

                            profiledocument.ArchivedAt = null;
                            break;
                        }
                    default:
                        return false;
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

    }
}
