using Azure.Storage.Blobs;
using System.Collections.Concurrent;

public class PromptManagerService : IPromptManagerService
{
    private readonly BlobContainerClient _blobContainerClient;
    private static readonly ConcurrentDictionary<string, string> _promptCache = new();

    public PromptManagerService(IConfiguration configuration)
    {
        // Uses the correct connection string key from your appsettings.json
#if DEBUG
        var connectionString = configuration.GetConnectionString("AzureBlobConnection");
#else
     var connectionString = Environment.GetEnvironmentVariable("AZURE_BLOB_KEY");
#endif

        _blobContainerClient = new BlobContainerClient(connectionString, "probuild-prompts");
    }

    public async Task<string> GetPromptAsync(string promptName)
    {
        if (_promptCache.TryGetValue(promptName, out var cachedPrompt)) return cachedPrompt;
        var blobClient = _blobContainerClient.GetBlobClient($"{promptName}.txt");
        if (!await blobClient.ExistsAsync()) throw new FileNotFoundException($"Prompt '{promptName}.txt' not found in Azure Blob Storage.");
        var response = await blobClient.DownloadContentAsync();
        var promptText = response.Value.Content.ToString();
        _promptCache.TryAdd(promptName, promptText);
        return promptText;
    }
}