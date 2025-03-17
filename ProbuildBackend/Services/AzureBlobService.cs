using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace ProbuildBackend.Services
{
    public class AzureBlobService
    {
        BlobServiceClient _blobClient;
        BlobContainerClient _containerClient;
        readonly string azureConnectionString = Environment.GetEnvironmentVariable("AZURE_BLOB_STORAGE_URL");
        public AzureBlobService()
        {
            if (string.IsNullOrEmpty(azureConnectionString))
            {
                Console.WriteLine("Warning: AZURE_BLOB_STORAGE_URL environment variable is not set. Defaulting to ''.");
            }
            else
            {
                _blobClient = new BlobServiceClient(azureConnectionString);
                _containerClient = _blobClient.GetBlobContainerClient("probuildaiprojects");
            }
        }


        public async Task UploadFiles(List<IFormFile> files)
        {
            var azureResponse = new List<Azure.Response<BlobContentInfo>>();
            foreach (var file in files)
            {
                string fileName = file.FileName;
                using (var memoryStream = new MemoryStream())
                {
                    await file.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;

                    // Check if the blob already exists
                    BlobClient blobClient = _containerClient.GetBlobClient(fileName);
                    if (await blobClient.ExistsAsync())
                    {
                        // Delete the existing blob if it exists
                        await blobClient.DeleteAsync();
                    }

                    // Upload the new blob
                    var response = await blobClient.UploadAsync(memoryStream, overwrite: true);
                    azureResponse.Add(response);
                }
            }
        }

        public async Task<List<BlobItem>> GetUploadedBlobs()
        {
            var items = new List<BlobItem>();
            var uploadedFiles = _containerClient.GetBlobsAsync();
            await foreach (BlobItem file in uploadedFiles)
            {
                items.Add(file);
            }

            return items;
        }
    }
}
