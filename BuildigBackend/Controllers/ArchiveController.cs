using Elastic.Apm.Api;
using Hangfire.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BuildigBackend.Interface;
using BuildigBackend.Models.DTO;
using BuildigBackend.Services;

namespace BuildigBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ArchiveController : ControllerBase
    {
        public readonly IArchiveService _archiveService;
        private readonly ApplicationDbContext _context;

        public ArchiveController(IArchiveService archiveService, ApplicationDbContext context)
        {
            _archiveService = archiveService;
            _context = context;
        }

        [HttpGet()]
        public async Task<IActionResult> GetArchivedItemsAsync([FromQuery] string userId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                    return BadRequest("User ID is required");

                var archivedItems = await _archiveService.GetArchivedItemsAsync(userId);

                if (archivedItems == null)
                    return NotFound();

                return Ok(archivedItems);
            }
            catch (Exception ex)
            {
                // Log the exception here
                return StatusCode(
                    500,
                    new { error = ex.Message, details = ex.InnerException?.Message }
                );
            }
        }

        [HttpPost("archiveJob")]
        public async Task<IActionResult> ArchiveJob([FromQuery] int jobId)
        {
            if (jobId == 0)
                return BadRequest("Id is required");

            var success = await _archiveService.ArchiveJob(jobId);

            if (!success)
                return BadRequest("Unable to archive item");

            return Ok(success);
        }

        [HttpPost("archivequoteinvoice")]
        public async Task<IActionResult> ArchiveQuoteOrInvoice([FromQuery] Guid itemId)
        {
            if (itemId == Guid.Empty)
                return BadRequest("Id is required");

            var success = await _archiveService.ArchiveQuoteOrInvoice(itemId);

            if (!success)
                return BadRequest("Unable to archive item");

            return Ok(success);
        }

        [HttpPost("unarchive")]
        public async Task<IActionResult> UnarchiveItem(
            [FromQuery] string itemId,
            [FromQuery] string itemType,
            [FromQuery] string userId
        )
        {
            if (string.IsNullOrEmpty(itemId))
                return BadRequest("Item ID is required");

            if (string.IsNullOrWhiteSpace(itemType))
                return BadRequest("Item type is required");

            if (string.IsNullOrWhiteSpace(userId))
                return BadRequest("User ID is required");

            var success = await _archiveService.UnarchiveAsync(itemId, itemType, userId);

            if (!success)
                return BadRequest("Unable to unarchive item");

            return Ok();
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteArchivedItem(
            [FromQuery] string itemId,
            [FromQuery] string itemType,
            [FromQuery] string userId
        )
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return BadRequest("Item ID is required");

            if (string.IsNullOrWhiteSpace(itemType))
                return BadRequest("Item type is required");

            if (string.IsNullOrWhiteSpace(userId))
                return BadRequest("User ID is required");

            var success = await _archiveService.DeleteArchivedItemAsync(itemId, itemType, userId);

            if (!success)
                return BadRequest("Unable to delete archived item");

            return Ok();
        }

        [HttpDelete]
        public async Task<IActionResult> EmptyArchive([FromQuery] string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return BadRequest("User ID is required");

            var success = await _archiveService.EmptyArchiveAsync(userId);

            if (!success)
                return BadRequest("Unable to empty archive");

            return Ok();
        }
    }
}

