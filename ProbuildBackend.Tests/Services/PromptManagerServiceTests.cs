using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Moq;
using System.Collections.Concurrent;
using System.Reflection;
using Xunit;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace ProbuildBackend.Tests.Services
{
    public class PromptManagerServiceUnitTests
    {
        private readonly IConfiguration _configuration;
        private readonly Mock<BlobContainerClient> _blobContainerClientMock;
        private readonly Mock<BlobClient> _blobClientMock;
        private readonly PromptManagerService _service;

        public PromptManagerServiceUnitTests()
        {
            // Build a real configuration object from the app's appsettings.json
            var basePath = AppContext.BaseDirectory;
            var projectRoot = Path.GetFullPath(Path.Combine(basePath, "..", "..", "..", "..", "ProbuildBackend"));
            var appSettingsPath = Path.Combine(projectRoot, "appsettings.json");

            if (!File.Exists(appSettingsPath))
            {
                throw new FileNotFoundException($"Could not find the main application's appsettings.json at: {appSettingsPath}");
            }

            _configuration = new ConfigurationBuilder()
                .AddJsonFile(appSettingsPath, optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            _blobClientMock = new Mock<BlobClient>();
            _service = new PromptManagerService(_configuration);

            _blobContainerClientMock = new Mock<BlobContainerClient>();
            _blobContainerClientMock.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(_blobClientMock.Object);

            var field = typeof(PromptManagerService).GetField("_blobContainerClient", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new InvalidOperationException("Cannot find field '_blobContainerClient' on PromptManagerService.");
            field.SetValue(_service, _blobContainerClientMock.Object);

            var cacheField = typeof(PromptManagerService).GetField("_promptCache", BindingFlags.NonPublic | BindingFlags.Static);
            if (cacheField != null)
            {
                var cache = (ConcurrentDictionary<string, string>?)cacheField.GetValue(null);
                cache?.Clear();
            }
        }

        [Fact]
        public async Task GetPromptAsync_ReturnsPromptFromBlob_WhenNotInCache()
        {
            var promptText = "This is a test prompt.";
            var blobDownloadResult = BlobsModelFactory.BlobDownloadResult(new BinaryData(promptText));
            _blobClientMock.Setup(b => b.ExistsAsync(default)).ReturnsAsync(Response.FromValue(true, new Mock<Response>().Object));
            _blobClientMock.Setup(b => b.DownloadContentAsync()).ReturnsAsync(Response.FromValue(blobDownloadResult, new Mock<Response>().Object));

            var result = await _service.GetPromptAsync("system", "generic-prompt.txt");

            Assert.Equal(promptText, result);
            _blobContainerClientMock.Verify(c => c.GetBlobClient("generic-prompt.txt"), Times.Once);
            _blobClientMock.Verify(b => b.DownloadContentAsync(), Times.Once);
        }

        [Fact]
        public async Task GetPromptAsync_ThrowsFileNotFoundException_WhenBlobDoesNotExist()
        {
            _blobClientMock.Setup(b => b.ExistsAsync(default)).ReturnsAsync(Response.FromValue(false, new Mock<Response>().Object));
            await Assert.ThrowsAsync<FileNotFoundException>(() => _service.GetPromptAsync("user", "nonexistent.txt"));
        }
    }
}
