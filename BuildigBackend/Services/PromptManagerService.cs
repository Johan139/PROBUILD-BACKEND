using System.Collections.Concurrent;
using System.Text;
using Azure.Storage.Blobs;
using BuildigBackend.Interface;

public class PromptManagerService : IPromptManagerService
{
    private readonly BlobContainerClient _blobContainerClient;
    private sealed record PromptCacheEntry(string Prompt, DateTime CachedAtUtc);
    private static readonly ConcurrentDictionary<string, PromptCacheEntry> _promptCache = new();
    private readonly ILogger<PromptManagerService> _logger;
    private readonly TimeSpan _promptCacheTtl = TimeSpan.FromMinutes(10);

    public PromptManagerService(IConfiguration configuration, ILogger<PromptManagerService> logger)
    {
#if DEBUG
        var connectionString = configuration.GetConnectionString("AzureBlobConnection");
        var containerName = configuration["PromptBlobContainerName"];
#else
        var connectionString = Environment.GetEnvironmentVariable("AZURE_BLOB_KEY");
        var containerName = Environment.GetEnvironmentVariable("PromptBlobContainerName");
#endif

        _blobContainerClient = new BlobContainerClient(connectionString, containerName);
        _logger = logger;
    }

    public async Task<string> GetPromptAsync(string userType, string fileName)
    {
        var fullBlobName = ResolvePromptBlobName(fileName);

        return await GetBlobTextAsync(fullBlobName);
    }

    public async Task<string> GetKnowledgeFileAsync(string fileName)
    {
        var normalized = NormalizeKnowledgeFileName(fileName);
        var fullBlobName = ResolveKnowledgeBlobName(normalized);
        return await GetBlobTextAsync(fullBlobName);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetKnowledgeFilesAsync(
        IEnumerable<string> fileNames
    )
    {
        var normalizedFiles = fileNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeKnowledgeFileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in normalizedFiles)
        {
            results[file] = await GetKnowledgeFileAsync(file);
        }

        return results;
    }

    private string ResolvePromptBlobName(string fileName)
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
        else if (fileName.Contains("-budget-"))
        {
            fullBlobName = $"BudgetPrompts/{fileName}";
        }
        else if (fileName.StartsWith("subcontractor-comparison"))
        {
            fullBlobName = $"ComparisonPrompts/{fileName}";
        }
        else
        {
            // System-level prompts are in the root
            fullBlobName = fileName;
        }

        return fullBlobName;
    }

    private static string NormalizeKnowledgeFileName(string fileName)
    {
        var normalized = fileName.Replace("\\", "/").TrimStart('/').Trim();
        if (normalized.StartsWith("Demo/docs/ai-knowledge/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("Demo/docs/ai-knowledge/".Length);
        }

        return normalized;
    }

    private static string ResolveKnowledgeBlobName(string fileName)
    {
        return $"Knowledge/{fileName}";
    }

    private async Task<string> GetBlobTextAsync(string fullBlobName)
    {
        _logger.LogInformation(
            "Attempting to get blob content with full blob name: {FullBlobName}",
            fullBlobName
        );

        if (_promptCache.TryGetValue(fullBlobName, out var cachedPrompt))
        {
            if (DateTime.UtcNow - cachedPrompt.CachedAtUtc <= _promptCacheTtl)
            {
                _logger.LogInformation("Found prompt in cache: {FullBlobName}", fullBlobName);
                return cachedPrompt.Prompt;
            }

            _promptCache.TryRemove(fullBlobName, out _);
            _logger.LogInformation("Found prompt in cache: {FullBlobName}", fullBlobName);
        }

        _logger.LogInformation(
            "Blob content not found in cache, fetching from Azure Blob Storage: {FullBlobName}",
            fullBlobName
        );
        var blobClient = _blobContainerClient.GetBlobClient(fullBlobName);
        if (!await blobClient.ExistsAsync())
        {
            _logger.LogError(
                "Prompt '{FullBlobName}' not found in Azure Blob Storage.",
                fullBlobName
            );
            throw new FileNotFoundException(
                $"Prompt '{fullBlobName}' not found in Azure Blob Storage."
            );
        }

        var response = await blobClient.DownloadContentAsync();
        var promptText = response.Value.Content.ToString();
        _promptCache[fullBlobName] = new PromptCacheEntry(promptText, DateTime.UtcNow);
        _logger.LogInformation(
            "Successfully fetched and cached prompt: {FullBlobName}",
            fullBlobName
        );
        return promptText;
    }
}

