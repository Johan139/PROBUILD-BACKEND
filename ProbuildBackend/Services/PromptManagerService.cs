using Azure.Storage.Blobs;
using ProbuildBackend.Interface;
using System.Collections.Concurrent;

public class PromptManagerService : IPromptManagerService
{
    private readonly BlobContainerClient _blobContainerClient;
    private static readonly ConcurrentDictionary<string, string> _promptCache = new();
    private readonly ILogger<PromptManagerService> _logger;

    public PromptManagerService(IConfiguration configuration, ILogger<PromptManagerService> logger)
    {
#if DEBUG
        var connectionString = configuration.GetConnectionString("AzureBlobConnection");
#else
     var connectionString = Environment.GetEnvironmentVariable("AZURE_BLOB_KEY");
#endif

        _blobContainerClient = new BlobContainerClient(connectionString, "probuild-prompts");
        _logger = logger;
    }

    public async Task<string> GetPromptAsync(string userType, string fileName)
    {
        string fullBlobName;

        // Determine the correct folder based on the file name convention
        if (fileName.EndsWith("_Subcontractor_Prompt.txt"))
        {
            fullBlobName = $"SubcontractorPrompts/{fileName}";
        }
        else if (fileName.StartsWith("renovation-"))
        {
            fullBlobName = $"RenovationPrompts/{fileName}";
        }
        else
        {
            // System-level prompts are in the root
            fullBlobName = fileName;
        }
        _logger.LogInformation("Attempting to get prompt with full blob name: {FullBlobName}", fullBlobName);

        if (_promptCache.TryGetValue(fullBlobName, out var cachedPrompt))
        {
            _logger.LogInformation("Found prompt in cache: {FullBlobName}", fullBlobName);
            return cachedPrompt;
        }

        _logger.LogInformation("Prompt not found in cache, fetching from Azure Blob Storage: {FullBlobName}", fullBlobName);
        var blobClient = _blobContainerClient.GetBlobClient(fullBlobName);
        if (!await blobClient.ExistsAsync())
        {
            _logger.LogError("Prompt '{FullBlobName}' not found in Azure Blob Storage.", fullBlobName);
            throw new FileNotFoundException($"Prompt '{fullBlobName}' not found in Azure Blob Storage.");
        }

        var response = await blobClient.DownloadContentAsync();
        var promptText = response.Value.Content.ToString();
        _promptCache.TryAdd(fullBlobName, promptText);
        _logger.LogInformation("Successfully fetched and cached prompt: {FullBlobName}", fullBlobName);
        return promptText;
    }
}
