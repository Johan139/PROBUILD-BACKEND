// ProbuildBackend/Interface/IPromptManagerService.cs
using System.Threading.Tasks;
public interface IPromptManagerService { Task<string> GetPromptAsync(string promptName); }