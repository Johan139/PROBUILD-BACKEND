using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.SignalR;
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

        public async Task<List<string>> UploadFiles(List<IFormFile> files, IHubContext<ProgressHub>? hubContext = null, string? connectionId = null)
        {
            try
            {
                _logger.LogInformation("Starting file upload process for {FileCount} files.", files.Count);
                var uploadedUrls = new List<string>();
                bool useSignalR = hubContext != null && !string.IsNullOrEmpty(connectionId);

                if (useSignalR)
                {
                    _logger.LogInformation("SignalR is enabled for this upload, using connectionId: {ConnectionId}", connectionId);
                }

                foreach (var file in files)
                {
                    string fileName = $"{Guid.NewGuid()}_{file.FileName}";
                    _logger.LogInformation("Generated unique file name: {FileName}", fileName);
                    BlobClient blobClient = _containerClient.GetBlobClient(fileName);

                    var blobHttpHeaders = new BlobHttpHeaders { ContentType = "application/gzip" };
                    string asciiFileName = new string(file.FileName
                    .Where(c => c <= 127) // Keep only ASCII characters
                    .ToArray());

                    var metadata = new Dictionary<string, string>
{
                    { "originalFileName", asciiFileName },
                    { "compression", "gzip" }
};

                    using var inputStream = file.OpenReadStream();
                    using var compressedStream = new MemoryStream();
                    using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
                    {
                        await inputStream.CopyToAsync(gzipStream);
                        gzipStream.Flush();
                        compressedStream.Position = 0;
                        _logger.LogInformation("Successfully compressed file: {OriginalFileName}", file.FileName);

                        var uploadOptions = new BlobUploadOptions
                        {
                            HttpHeaders = blobHttpHeaders,
                            Metadata = metadata,
                            TransferOptions = new StorageTransferOptions
                            {
                                MaximumTransferSize = 4 * 1024 * 1024 // 4MB chunks
                            }
                        };

                        if (useSignalR)
                        {
                            long totalBytes = compressedStream.Length;
                            var progressHandler = new Progress<long>(async progress =>
                            {
                                int progressPercent = totalBytes > 0 ? (int)Math.Min(100, (progress * 100) / totalBytes) : 0;
                                await hubContext!.Clients.Client(connectionId!).SendAsync("ReceiveProgress", progressPercent);
                            });
                            uploadOptions.ProgressHandler = progressHandler;
                        }

                        await blobClient.UploadAsync(compressedStream, uploadOptions);
                        _logger.LogInformation("Successfully uploaded file to Azure Blob Storage: {FileName}", fileName);

                        string blobUrl = $"https://qastorageprobuildaiblob.blob.core.windows.net/probuildaiprojects/{fileName}";
                        uploadedUrls.Add(blobUrl);
                    }
                }

                if (useSignalR)
                {
                    await hubContext!.Clients.Client(connectionId!).SendAsync("UploadComplete", files.Count);
                    _logger.LogInformation("Sent 'UploadComplete' SignalR message to client: {ConnectionId}", connectionId);
                }

                _logger.LogInformation("File upload process complete. Returning {FileCount} URLs.", uploadedUrls.Count);
                return uploadedUrls;
            }
            catch (Exception ex)
            {

                throw;
            }
        }
        public async Task<string> UploadFileAsync(string fileName, Stream content, string contentType, int? jobId = null)
        {
            string blobName = jobId.HasValue
                ? $"job_{jobId}/blueprints/{Guid.NewGuid()}_{fileName}"
                : $"{Guid.NewGuid()}_{fileName}";

            _logger.LogInformation("Uploading to blob: {BlobName}", blobName);
            BlobClient blobClient = _containerClient.GetBlobClient(blobName);

            var blobHttpHeaders = new BlobHttpHeaders { ContentType = contentType };

            var metadata = new Dictionary<string, string>
            {
                { "originalFileName", fileName }
            };

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = blobHttpHeaders,
                Metadata = metadata,
                TransferOptions = new StorageTransferOptions
                {
                    MaximumTransferSize = 4 * 1024 * 1024 // 4MB chunks
                }
            };

            await blobClient.UploadAsync(content, uploadOptions);
            _logger.LogInformation("Successfully uploaded file to Azure Blob Storage: {BlobName}", blobName);

            return blobClient.Uri.ToString();
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
                var decodedBlobUrl = Uri.UnescapeDataString(blobUrl);
                var uri = new Uri(decodedBlobUrl);
                var containerName = uri.Segments[1].TrimEnd('/');

                // The blob name is the entire path after the container name.
                var blobName = uri.AbsolutePath.Substring(containerName.Length + 2); // +2 for the two slashes

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
            string fullPath = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            string[] segments = fullPath.Split('/', 2); // Split into container and blob name

            if (segments.Length != 2)
                throw new InvalidOperationException("Invalid blob URL format.");

            string containerName = segments[0];
            string blobName = segments[1]; // Use the remaining string as-is
            Console.WriteLine(blobName);
            var containerClient = _blobClient.GetBlobContainerClient(containerName);
            Console.WriteLine(containerClient);
            var blobClient = containerClient.GetBlobClient(blobName);

            Console.WriteLine($"Full Blob Path: {blobClient.Uri}");
            Console.WriteLine(blobClient.Uri + " " + blobClient.Name);
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

        public async Task<string> UploadImageAsync(IFormFile file, string folder)
        {
            string fileName = $"{Guid.NewGuid()}_{file.FileName}";
            string blobName = $"{folder}/{fileName}";
            BlobClient blobClient = _containerClient.GetBlobClient(blobName);

            var blobHttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType };

            var metadata = new Dictionary<string, string>
            {
                { "originalFileName", file.FileName }
            };

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = blobHttpHeaders,
                Metadata = metadata
            };

            using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, uploadOptions);

            return blobClient.Uri.ToString();
        }
    }
}
