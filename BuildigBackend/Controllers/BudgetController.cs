using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using BuildigBackend.Interface;
using BuildigBackend.Models;

namespace BuildigBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BudgetController : ControllerBase
    {
        private readonly IBudgetService _budgetService;
        private readonly ILogger<BudgetController> _logger;
        private readonly IMemoryCache _cache;

        public BudgetController(
            IBudgetService budgetService,
            ILogger<BudgetController> logger,
            IMemoryCache cache
        )
        {
            _budgetService = budgetService;
            _logger = logger;
            _cache = cache;
        }

        private static string GetBudgetCacheKey(int jobId) => $"budget:items:{jobId}";

        [HttpGet("{jobId}")]
        public async Task<ActionResult<IEnumerable<BudgetLineItem>>> GetBudget(int jobId)
        {
            var correlationId = Request.Headers.TryGetValue("X-Correlation-Id", out var cid)
                ? cid.ToString()
                : (HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N"));
            var sw = Stopwatch.StartNew();
            if (_cache.TryGetValue(GetBudgetCacheKey(jobId), out List<BudgetLineItem>? cachedItems) && cachedItems != null)
            {
                sw.Stop();
                _logger.LogInformation(
                    "GetBudget cache-hit jobId={JobId} elapsedMs={ElapsedMs} count={Count} correlationId={CorrelationId}",
                    jobId,
                    sw.ElapsedMilliseconds,
                    cachedItems.Count,
                    correlationId
                );
                return Ok(cachedItems);
            }

            var items = await _budgetService.GetBudgetItemsByJobIdAsync(jobId);
            var list = items?.ToList() ?? new List<BudgetLineItem>();
            _cache.Set(GetBudgetCacheKey(jobId), list, TimeSpan.FromSeconds(15));
            sw.Stop();
            _logger.LogInformation(
                "GetBudget success jobId={JobId} elapsedMs={ElapsedMs} dbApproxMs={DbApproxMs} count={Count} correlationId={CorrelationId}",
                jobId,
                sw.ElapsedMilliseconds,
                sw.ElapsedMilliseconds,
                list.Count,
                correlationId
            );
            return Ok(list);
        }

        [HttpPost]
        public async Task<ActionResult<BudgetLineItem>> AddBudgetItem(BudgetLineItem item)
        {
            if (item == null)
                return BadRequest("Budget item cannot be null");
            var created = await _budgetService.AddBudgetItemAsync(item);
            _cache.Remove(GetBudgetCacheKey(created.JobId));
            return CreatedAtAction(nameof(GetBudget), new { jobId = created.JobId }, created);
        }

        [HttpPost("batch")]
        public async Task<ActionResult<IEnumerable<BudgetLineItem>>> AddBudgetItemsBatch(
            IEnumerable<BudgetLineItem> items
        )
        {
            if (items == null || !items.Any())
                return BadRequest("Budget items cannot be null or empty");
            var created = await _budgetService.AddBudgetItemsAsync(items);
            var first = created.FirstOrDefault();
            if (first != null)
            {
                _cache.Remove(GetBudgetCacheKey(first.JobId));
            }
            return Ok(created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateBudgetItem(int id, BudgetLineItem item)
        {
            if (item == null || id != item.Id)
                return BadRequest();
            var updated = await _budgetService.UpdateBudgetItemAsync(id, item);
            if (updated == null)
                return NotFound();
            _cache.Remove(GetBudgetCacheKey(updated.JobId));
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBudgetItem(int id)
        {
            var success = await _budgetService.DeleteBudgetItemAsync(id);
            if (!success)
                return NotFound();
            // Cache may remain stale for up to 15s on delete-by-id endpoint.
            return NoContent();
        }

        [HttpPost("{jobId}/sync")]
        public async Task<IActionResult> SyncBudget(int jobId)
        {
            try
            {
                await _budgetService.SyncBudgetFromJobAsync(jobId);
                _cache.Remove(GetBudgetCacheKey(jobId));
                return Ok(new { message = "Budget synced successfully" });
            }
            catch (System.Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}

