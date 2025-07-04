using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using iText.Commons.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using ProbuildBackend.Middleware;
using System.IO.Compression;

namespace ProbuildBackend.Services
{
    public class AzureBlobService
    {
        private readonly BlobServiceClient _blobClient;
        private readonly BlobContainerClient _containerClient;
        private readonly string _containerName = "probuildaiprojects";
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AzureBlobService> _logger;

        public AzureBlobService(IConfiguration configuration, IHttpContextAccessor httpContextAccessor, ILogger<AzureBlobService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            var azureConnectionString = Environment.GetEnvironmentVariable("AZURE_BLOB_KEY")
                      ?? configuration["ConnectionStrings:AzureBlobConnection"];
            if (string.IsNullOrEmpty(azureConnectionString))
            {
                throw new ArgumentNullException(nameof(azureConnectionString), "Azure Blob Storage connection string is not configured");
            }

            _blobClient = new BlobServiceClient(azureConnectionString);
            _containerClient = _blobClient.GetBlobContainerClient(_containerName);
            InitializeContainerAsync().GetAwaiter().GetResult();
        }

        private async Task InitializeContainerAsync()
        {
            await _containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);
        }
        public async Task DeleteTemporaryFiles(List<string> blobUrls)
        {
            foreach (var blobUrl in blobUrls)
            {
                try
                {
                    var blobUri = new Uri(blobUrl);
                    // Decode the blob name to match the actual name in Azure Blob Storage
                    var blobName = Uri.UnescapeDataString(blobUri.AbsolutePath.TrimStart('/').Replace($"{_containerName}/", ""));
                    Console.WriteLine($"Deleting blob: {blobName}");

                    var blobClient = _containerClient.GetBlobClient(blobName);
                    var response = await blobClient.DeleteIfExistsAsync();
                    Console.WriteLine($"Blob {blobName} deleted: {response.Value}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deleting blob {blobUrl}: {ex.Message}");
                    throw;
                }
            }
        }

        public async Task<List<string>> UploadFiles(List<IFormFile> files, IHubContext<ProgressHub> hubContext, string connectionId)
        {
            var uploadedUrls = new List<string>();
            if (string.IsNullOrEmpty(connectionId))
            {
                throw new InvalidOperationException("No valid connectionId provided.");
            }

            Console.WriteLine($"Using connectionId for SignalR: {connectionId}");

            foreach (var file in files)
            {
                string fileName = $"{Guid.NewGuid()}_{file.FileName}"; // e.g., "c09444bf-9180-4179-a76a-d3be18043b8a_Trophy Club Approved Plans 072324 (1).pdf"
                BlobClient blobClient = _containerClient.GetBlobClient(fileName);

                var blobHttpHeaders = new BlobHttpHeaders { ContentType = "application/gzip" };
                var metadata = new Dictionary<string, string>
        {
            { "originalFileName", file.FileName },
            { "compression", "gzip" }
        };

                using var inputStream = file.OpenReadStream();
                using var compressedStream = new MemoryStream();
                using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
                {
                    await inputStream.CopyToAsync(gzipStream);
                    gzipStream.Flush();
                    compressedStream.Position = 0;

                    long totalBytes = compressedStream.Length; // Compressed size
                    long bytesUploaded = 0;

                    var progressHandler = new Progress<long>(async progress =>
                    {
                        bytesUploaded = progress;
                        int progressPercent = (int)Math.Min(100, (bytesUploaded * 100) / totalBytes);
                        Console.WriteLine($"Azure Upload Progress: {progressPercent}% for {file.FileName} (Bytes: {bytesUploaded}/{totalBytes})");
                        await hubContext.Clients.Client(connectionId).SendAsync("ReceiveProgress", progressPercent);
                    });

                    await blobClient.UploadAsync(compressedStream, new BlobUploadOptions
                    {
                        HttpHeaders = blobHttpHeaders,
                        Metadata = metadata,
                        TransferOptions = new StorageTransferOptions
                        {
                            MaximumTransferSize = 4 * 1024 * 1024 // 4MB chunks
                        },
                        ProgressHandler = progressHandler // Track Azure upload progress
                    });

                    // Build the URL manually to avoid encoding
                    string blobUrl = $"https://qastorageprobuildaiblob.blob.core.windows.net/probuildaiprojects/{fileName}";
                    uploadedUrls.Add(blobUrl);
                    Console.WriteLine($"Generated Blob URL: {blobUrl}");
                }
            }

            Console.WriteLine("Upload complete, sending UploadComplete signal");
            await hubContext.Clients.Client(connectionId).SendAsync("UploadComplete", files.Count);
            return uploadedUrls;
        }

        public async Task<List<BlobItem>> GetUploadedBlobs()
        {
            var items = new List<BlobItem>();
            await foreach (BlobItem file in _containerClient.GetBlobsAsync())
            {
                items.Add(file);
            }
            return items;
        }
        // New method to get blob properties
        public async Task<BlobProperties> GetBlobProperties(string blobUrl)
        {
            try
            {
                var uri = new Uri(blobUrl);
                var blobName = uri.Segments.Last();
                var blobContainerClient = _blobClient.GetBlobContainerClient(_containerName);
                var blobClient = blobContainerClient.GetBlobClient(blobName);

                var properties = await blobClient.GetPropertiesAsync();
                return properties.Value;
            }
            catch (Exception)
            {

                throw;
            }
        }
        public async Task<(Stream Content, string ContentType, string OriginalFileName)> GetBlobContentAsync(string blobUrl)
        {
            try
            {
                // Step 1: Extract the blob name from the URL
                var uri = new Uri(blobUrl);
                var blobName = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/').Replace($"{_containerName}/", ""));
                Console.WriteLine($"Fetching blob: {blobName}");

                // Step 2: Get the BlobClient
                var blobClient = _containerClient.GetBlobClient(blobName);

                // Step 3: Check if the blob exists
                bool exists = await blobClient.ExistsAsync();
                if (!exists)
                {
                    throw new FileNotFoundException($"Blob '{blobName}' does not exist in container '{_containerName}'.");
                }

                // Step 4: Download the blob content
                BlobDownloadInfo download = await blobClient.DownloadAsync();

                // Step 5: Get metadata to determine the original file name and content type
                var properties = await blobClient.GetPropertiesAsync();
                var metadata = properties.Value.Metadata;
                string originalFileName = metadata.ContainsKey("originalFileName") ? metadata["originalFileName"] : blobName;
                bool isCompressed = metadata.ContainsKey("compression") && metadata["compression"] == "gzip";

                // Step 6: Decompress the content if it was compressed
                Stream contentStream;
                string contentType;

                if (isCompressed)
                {
                    // Decompress the gzip content
                    var decompressedStream = new MemoryStream();
                    using (var gzipStream = new GZipStream(download.Content, CompressionMode.Decompress))
                    {
                        await gzipStream.CopyToAsync(decompressedStream);
                    }
                    decompressedStream.Position = 0;
                    contentStream = decompressedStream;

                    // Set the content type based on the original file (e.g., PDF)
                    contentType = originalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "application/octet-stream";
                }
                else
                {
                    // If not compressed, use the content directly
                    contentStream = download.Content;
                    contentType = properties.Value.ContentType;
                }

                return (contentStream, contentType, originalFileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching blob content for {blobUrl}: {ex.Message}");
                throw;
            }
        }
        public BlobClient GetBlobClient(string fileName)
        {
            return _containerClient.GetBlobClient(fileName);
        }

        public string GenerateTemporaryPublicUrl(string blobUrl)
        {
            try
            {
                var uri = new Uri(blobUrl);
                var containerName = uri.Segments[1].TrimEnd('/');
                var blobName = uri.AbsolutePath.Substring(uri.AbsolutePath.IndexOf('/', 1) + 1);

                var containerClient = _blobClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                if (!blobClient.CanGenerateSasUri)
                {
                    _logger.LogError("BlobClient cannot generate SAS URI. Check account permissions.");
                    return blobClient.Uri.ToString(); // Fallback to raw URL
                }

                var sasBuilder = new BlobSasBuilder()
                {
                    BlobContainerName = containerName,
                    BlobName = blobName,
                    Resource = "b", // 'b' for a single blob
                    StartsOn = DateTimeOffset.UtcNow,
                    ExpiresOn = DateTimeOffset.UtcNow.AddHours(1), // Grant access for 1 hour
                };

                // Specify Read permissions for the SAS token
                sasBuilder.SetPermissions(BlobSasPermissions.Read);

                // Generate the SAS URI and return it as a string
                return blobClient.GenerateSasUri(sasBuilder).ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate SAS URI for blob: {BlobUrl}", blobUrl);
                return blobUrl; // Fallback to the original URL on error
            }
        }

        public async Task<(byte[] Content, string ContentType)> DownloadBlobAsBytesAsync(string blobUrl)
        {
            var uri = new Uri(blobUrl);
            string fullPath = uri.AbsolutePath.TrimStart('/');
            string containerName = fullPath.Split('/')[0];
            string blobName = fullPath.Substring(containerName.Length + 1);

            var containerClient = _blobClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                _logger.LogError("Blob not found for download at Container: {Container}, Blob: {Blob}", containerName, blobName);
                throw new FileNotFoundException("Blob not found for download.", blobName);
            }

            // Step 1: Download the blob's info and content stream
            BlobDownloadInfo download = await blobClient.DownloadAsync();
            var properties = await blobClient.GetPropertiesAsync();
            var metadata = properties.Value.Metadata;

            // Step 2: Check if the blob is compressed
            bool isCompressed = metadata.ContainsKey("compression") && metadata["compression"] == "gzip";

            if (isCompressed)
            {
                // Step 3a: If compressed, decompress the content into a new memory stream
                using var decompressedStream = new MemoryStream();
                using (var gzipStream = new GZipStream(download.Content, CompressionMode.Decompress))
                {
                    await gzipStream.CopyToAsync(decompressedStream);
                }

                // Determine the original content type (e.g., application/pdf)
                string originalFileName = metadata.ContainsKey("originalFileName") ? metadata["originalFileName"] : blobName;
                string originalContentType = originalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                    ? "application/pdf"
                    : "application/octet-stream"; // Fallback

                // Return the DECOMPRESSED bytes and the ORIGINAL content type
                return (decompressedStream.ToArray(), originalContentType);
            }
            else
            {
                // Step 3b: If not compressed, download directly and return
                using var memoryStream = new MemoryStream();
                await download.Content.CopyToAsync(memoryStream);
                return (memoryStream.ToArray(), properties.Value.ContentType);
            }
        }
    }
}