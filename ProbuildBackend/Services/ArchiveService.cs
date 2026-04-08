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
                var archivedJobs = await _context
                    .Jobs.Where(j => j.ArchivedAt != null && j.UserId == userId)
                    .Select(j => new ArchivedItemDto
                    {
                        Id = j.Id.ToString(),
                        Type = "JOB",
                        Title = j.ProjectName ?? "",
                        Status = j.Status ?? "",
                        ArchivedAt = j.ArchivedAt ?? DateTime.MinValue,
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

                var archivedTradePackages = await _context.TradePackages
    .Where(tp => tp.ArchivedAt != null)
    .Join(
        _context.Jobs.Where(j => j.UserId == userId),
        tp => tp.JobId,
        j => j.Id,
        (tp, j) => new ArchivedItemDto
        {
            Id = tp.Id.ToString(),
            JobId = "job-" + tp.JobId.ToString(),
            Type = "TRADE_PACKAGE",
            Title = tp.ScopeOfWork ?? tp.TradeName ?? "Untitled Job",
            TradeName = tp.TradeName ?? tp.Category ?? "",
            Status = tp.Status ?? "Posted",
            BidsCount = _context.Bids.Count(b => b.TradePackageId == tp.Id),
            ArchivedAt = tp.ArchivedAt ?? DateTime.MinValue
        }
    )
    .ToListAsync();
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
         .Concat(archivedTradePackages)
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
                var quoteOrinvoice = await _context
                    .Quotes.Where(q => q.Id == id)
                    .FirstOrDefaultAsync();
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
                        var job = await _context.Jobs.FirstOrDefaultAsync(j =>
                            j.Id == Convert.ToInt32(itemId) && j.UserId == userId
                        );

                        if (job == null)
                            return false;

                        job.ArchivedAt = null;
                        break;
                    }

                    case "QUOTE":
                    case "INVOICE":
                    {
                        var quote = await _context.Quotes.FirstOrDefaultAsync(q =>
                            q.Id == Guid.Parse(itemId) && q.CreatedID == userId
                        );

                        if (quote == null)
                            return false;

                        quote.ArchivedAt = null;
                        break;
                    }
                    case "DOCUMENT":
                    {
                        var profiledocument = await _context.ProfileDocuments.FirstOrDefaultAsync(
                            q => q.Id == Convert.ToInt32(itemId)
                        );

                        if (profiledocument == null)
                            return false;

                        profiledocument.ArchivedAt = null;
                        break;
                    }
                    case "TRADE_PACKAGE":
                        {
                            var tradePackage = await _context.TradePackages.FirstOrDefaultAsync(t => t.Id == Convert.ToInt32(itemId));

                            if (tradePackage == null) return false;

                            tradePackage.ArchivedAt = null;
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

        public async Task<bool> DeleteArchivedItemAsync(string itemId, string itemType, string userId)
        {
            try
            {
                switch (itemType.ToUpper())
                {
                    case "JOB":
                    {
                        var jobId = Convert.ToInt32(itemId);
                        var job = await _context.Jobs.FirstOrDefaultAsync(j =>
                            j.Id == jobId && j.UserId == userId && j.ArchivedAt != null
                        );

                        if (job == null)
                            return false;

                        var processingResults = await _context
                            .DocumentProcessingResults.Where(x => x.JobId == jobId)
                            .ToListAsync();
                        _context.DocumentProcessingResults.RemoveRange(processingResults);

                        var jobAddresses = await _context
                            .JobAddresses.Where(x => x.JobId == jobId)
                            .ToListAsync();
                        _context.JobAddresses.RemoveRange(jobAddresses);

                        var assignments = await _context
                            .JobAssignments.Where(x => x.JobId == jobId)
                            .ToListAsync();
                        _context.JobAssignments.RemoveRange(assignments);

                        var jobDocuments = await _context
                            .JobDocuments.Where(x => x.JobId == jobId)
                            .ToListAsync();
                        _context.JobDocuments.RemoveRange(jobDocuments);

                        var termsAgreements = await _context
                            .JobsTermsAgreement.Where(x => x.JobId == jobId)
                            .ToListAsync();
                        _context.JobsTermsAgreement.RemoveRange(termsAgreements);

                        var jobSubtasks = await _context
                            .JobSubtasks.Where(x => x.JobId == jobId)
                            .ToListAsync();
                        _context.JobSubtasks.RemoveRange(jobSubtasks);

                        var notifications = await _context
                            .Notifications.Where(x => x.JobId == jobId)
                            .ToListAsync();
                        _context.Notifications.RemoveRange(notifications);

                        var tradeBudgets = await _context
                            .JobTradeBudgets.Where(x => x.JobId == jobId)
                            .ToListAsync();
                        _context.JobTradeBudgets.RemoveRange(tradeBudgets);

                        var permits = await _context
                            .JobPermits.Where(x => x.JobId == jobId)
                            .ToListAsync();
                        _context.JobPermits.RemoveRange(permits);

                        var tradePackages = await _context
                            .TradePackages.Where(x => x.JobId == jobId)
                            .ToListAsync();
                        _context.TradePackages.RemoveRange(tradePackages);

                        _context.Jobs.Remove(job);
                        break;
                    }
                    case "QUOTE":
                    case "INVOICE":
                    {
                        var quoteId = Guid.Parse(itemId);
                        var quote = await _context.Quotes.FirstOrDefaultAsync(q =>
                            q.Id == quoteId && q.CreatedID == userId && q.ArchivedAt != null
                        );

                        if (quote == null)
                            return false;


                            quote.ArchivedAt = null;
                            break;
                        }
                    case "TRADE_PACKAGE":
                        {
                            var tp = await _context.TradePackages
                                .FirstOrDefaultAsync(t => t.Id == Convert.ToInt32(itemId));

                            if (tp == null) return false;

                            tp.ArchivedAt = null;
                            break;
                        }

                    case "DOCUMENT":
                    {
                        var documentId = Convert.ToInt32(itemId);
                        var profileDocument = await _context.ProfileDocuments.FirstOrDefaultAsync(d =>
                            d.Id == documentId && d.UserId == userId && d.ArchivedAt != null
                        );

                        if (profileDocument == null)
                            return false;

                        _context.ProfileDocuments.Remove(profileDocument);
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

        public async Task<bool> EmptyArchiveAsync(string userId)
        {
            try
            {
                var archivedItems = await GetArchivedItemsAsync(userId);

                foreach (var item in archivedItems)
                {
                    var deleted = await DeleteArchivedItemAsync(item.Id, item.Type, userId);
                    if (!deleted)
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
