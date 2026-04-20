namespace BuildigBackend.Interface
{
    public interface IPromptManagerService
    {
        Task<string> GetPromptAsync(string userType, string fileName);
        Task<string> GetKnowledgeFileAsync(string fileName);
        Task<IReadOnlyDictionary<string, string>> GetKnowledgeFilesAsync(
            IEnumerable<string> fileNames
        );
    }
}

