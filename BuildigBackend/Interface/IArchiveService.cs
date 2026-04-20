using Microsoft.AspNetCore.Mvc;
using BuildigBackend.Models.DTO;

namespace BuildigBackend.Interface
{
    public interface IArchiveService
    {
        Task<List<ArchivedItemDto>> GetArchivedItemsAsync([FromQuery] string userId);
        Task<bool> UnarchiveAsync(string itemId, string itemType, string userId);
        Task<bool> DeleteArchivedItemAsync(string itemId, string itemType, string userId);
        Task<bool> EmptyArchiveAsync(string userId);
        Task<bool> ArchiveQuoteOrInvoice(Guid id);
        Task<bool> ArchiveJob(int jobid);
    }
}

