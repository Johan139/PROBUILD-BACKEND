using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;

namespace ProbuildBackend.Controllers
{
  [Route("api/[controller]")]
  [ApiController]
  public class BudgetController : ControllerBase
  {
    private readonly IBudgetService _budgetService;

    public BudgetController(IBudgetService budgetService)
    {
      _budgetService = budgetService;
    }

    [HttpGet("{jobId}")]
    public async Task<ActionResult<IEnumerable<BudgetLineItem>>> GetBudget(int jobId)
    {
      var items = await _budgetService.GetBudgetItemsByJobIdAsync(jobId);
      return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<BudgetLineItem>> AddBudgetItem(BudgetLineItem item)
    {
      if (item == null) return BadRequest("Budget item cannot be null");
      var created = await _budgetService.AddBudgetItemAsync(item);
      return CreatedAtAction(nameof(GetBudget), new { jobId = created.JobId }, created);
    }

    [HttpPost("batch")]
    public async Task<ActionResult<IEnumerable<BudgetLineItem>>> AddBudgetItemsBatch(IEnumerable<BudgetLineItem> items)
    {
      if (items == null || !items.Any()) return BadRequest("Budget items cannot be null or empty");
      var created = await _budgetService.AddBudgetItemsAsync(items);
      return Ok(created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBudgetItem(int id, BudgetLineItem item)
    {
      if (item == null || id != item.Id) return BadRequest();
      var updated = await _budgetService.UpdateBudgetItemAsync(id, item);
      if (updated == null) return NotFound();
      return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBudgetItem(int id)
    {
      var success = await _budgetService.DeleteBudgetItemAsync(id);
      if (!success) return NotFound();
      return NoContent();
    }

    [HttpPost("{jobId}/sync")]
    public async Task<IActionResult> SyncBudget(int jobId)
    {
      try
      {
        await _budgetService.SyncBudgetFromJobAsync(jobId);
        return Ok(new { message = "Budget synced successfully" });
      }
      catch (System.Exception ex)
      {
        return BadRequest(new { error = ex.Message });
      }
    }
  }
}
