// ProbuildBackend/Interface/IPromptManagerService.cs
public interface IPromptManagerService { Task<string> GetPromptAsync(string folderPath, string fileName); }