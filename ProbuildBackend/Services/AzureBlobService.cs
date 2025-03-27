using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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

        public AzureBlobService(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            var azureConnectionString = Environment.GetEnvironmentVariable("AZURE_BLOB_KEY");
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
                string fileName = $"{Guid.NewGuid()}_{file.FileName}";
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

                    uploadedUrls.Add(blobClient.Uri.ToString());
                }
            }

            Console.WriteLine("Upload complete, sending UploadComplete signal");
            // Send the number of files uploaded along with the completion signal
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

        public BlobClient GetBlobClient(string fileName)
        {
            return _containerClient.GetBlobClient(fileName);
        }
    }
}