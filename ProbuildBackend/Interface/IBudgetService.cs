using System.Collections.Generic;
using System.Threading.Tasks;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Interface
{
  public interface IBudgetService
  {
    Task<IEnumerable<BudgetLineItem>> GetBudgetItemsByJobIdAsync(int jobId);
    Task<BudgetLineItem> AddBudgetItemAsync(BudgetLineItem item);
    Task<BudgetLineItem> UpdateBudgetItemAsync(int id, BudgetLineItem item);
    Task<bool> DeleteBudgetItemAsync(int id);
    Task SyncBudgetFromJobAsync(int jobId);
  }
}