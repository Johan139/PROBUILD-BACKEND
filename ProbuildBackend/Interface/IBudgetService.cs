using ProbuildBackend.Models;

namespace ProbuildBackend.Interface
{
    public interface IBudgetService
    {
        Task<IEnumerable<BudgetLineItem>> GetBudgetItemsByJobIdAsync(int jobId);
        Task<BudgetLineItem> AddBudgetItemAsync(BudgetLineItem item);
        Task<IEnumerable<BudgetLineItem>> AddBudgetItemsAsync(IEnumerable<BudgetLineItem> items);
        Task<BudgetLineItem> UpdateBudgetItemAsync(int id, BudgetLineItem item);
        Task<bool> DeleteBudgetItemAsync(int id);
        Task SyncBudgetFromJobAsync(int jobId);
    }
}
