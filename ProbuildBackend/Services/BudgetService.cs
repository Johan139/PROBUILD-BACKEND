using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;

namespace ProbuildBackend.Services
{
  public class BudgetService : IBudgetService
  {
    private readonly ApplicationDbContext _context;

    public BudgetService(ApplicationDbContext context)
    {
      _context = context;
    }

    public async Task<IEnumerable<BudgetLineItem>> GetBudgetItemsByJobIdAsync(int jobId)
    {
      return await _context.BudgetLineItems
          .Where(b => b.JobId == jobId)
          .OrderBy(b => b.CreatedAt)
          .ToListAsync();
    }

    public async Task<BudgetLineItem> AddBudgetItemAsync(BudgetLineItem item)
    {
      _context.BudgetLineItems.Add(item);
      await _context.SaveChangesAsync();
      return item;
    }

    public async Task<BudgetLineItem> UpdateBudgetItemAsync(int id, BudgetLineItem item)
    {
      var existing = await _context.BudgetLineItems.FindAsync(id);
      if (existing == null) return null;

      existing.Category = item.Category;
      existing.Item = item.Item;
      existing.Trade = item.Trade;
      existing.EstimatedCost = item.EstimatedCost;
      existing.ActualCost = item.ActualCost;
      existing.PercentComplete = item.PercentComplete;
      existing.Status = item.Status;
      existing.Notes = item.Notes;
      existing.UpdatedAt = DateTime.UtcNow;

      await _context.SaveChangesAsync();
      return existing;
    }

    public async Task<bool> DeleteBudgetItemAsync(int id)
    {
      var item = await _context.BudgetLineItems.FindAsync(id);
      if (item == null) return false;

      _context.BudgetLineItems.Remove(item);
      await _context.SaveChangesAsync();
      return true;
    }

    public async Task SyncBudgetFromJobAsync(int jobId)
    {
      var job = await _context.Jobs.FindAsync(jobId);
      if (job == null) throw new Exception("Job not found");

      var subtasks = new List<SubtaskDto>();

      // Helper to parse JSON subtasks safely
      void AddSubtasks(string json)
      {
        if (!string.IsNullOrEmpty(json))
        {
          try
          {
            var parsed = JsonConvert.DeserializeObject<List<SubtaskDto>>(json);
            if (parsed != null) subtasks.AddRange(parsed);
          }
          catch { /* Ignore parsing errors */ }
        }
      }

      AddSubtasks(job.WallStructureSubtask);
      AddSubtasks(job.WallInsulationSubtask);
      AddSubtasks(job.RoofStructureSubtask);
      AddSubtasks(job.RoofInsulationSubtask);
      AddSubtasks(job.FoundationSubtask);
      AddSubtasks(job.FinishesSubtask);
      AddSubtasks(job.ElectricalSupplyNeedsSubtask);
      // Add other subtask fields as needed...

      foreach (var task in subtasks)
      {
        // Only sync if cost > 0
        if (task.Cost <= 0) continue;

        // Check if already synced
        var existingItem = await _context.BudgetLineItems
            .FirstOrDefaultAsync(b => b.JobId == jobId && b.Source == "Subtask" && b.SourceId == task.Id.ToString());

        if (existingItem != null)
        {
          // Update estimated cost if changed? 
          // Ideally user edits might override, but "Sync" implies refreshing from source TODO: decide on policy
          if (existingItem.EstimatedCost != task.Cost)
          {
            existingItem.EstimatedCost = task.Cost;
            existingItem.UpdatedAt = DateTime.UtcNow;
          }
        }
        else
        {
          var newItem = new BudgetLineItem
          {
            JobId = jobId,
            Category = "Labor", // Default for subtasks
            Item = task.Task,
            EstimatedCost = task.Cost,
            ActualCost = 0,
            PercentComplete = 0,
            Status = task.Status ?? "Pending",
            Source = "Subtask",
            SourceId = task.Id.ToString(),
            Trade = "General" // Could infer from subtask group if we had that info here TODO: improve
          };
          _context.BudgetLineItems.Add(newItem);
        }
      }

      await _context.SaveChangesAsync();
    }

    // Internal DTO for parsing subtasks JSON
    private class SubtaskDto
    {
      public int Id { get; set; }
      public string Task { get; set; }
      public decimal Cost { get; set; }
      public string Status { get; set; }
    }
  }
}