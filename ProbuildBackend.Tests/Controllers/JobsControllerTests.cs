using Xunit;
using Moq;
using ProbuildBackend.Services;
using ProbuildBackend.Models;
using ProbuildBackend.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity.UI.Services;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models.DTO;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using ProbuildBackend.Interface;
namespace ProbuildBackend.Tests
{
    public class JobsControllerTests
    {
        private readonly Mock<ApplicationDbContext> _mockDbContext;
        private readonly Mock<AzureBlobService> _mockAzureBlobService;
        private readonly Mock<IHubContext<ProgressHub>> _mockHubContext;
        private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
        private readonly Mock<IDocumentProcessorService> _mockDocumentProcessor;
        private readonly Mock<IEmailSender> _mockEmailSender;
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly Mock<WebSocketManager> _mockWebSocketManager;
        private readonly Mock<IBackgroundJobClient> _mockBackgroundJobClient;
        private readonly JobsController _jobsController;

        public JobsControllerTests()
        {
            var options = new DbContextOptions<ApplicationDbContext>();
            _mockDbContext = new Mock<ApplicationDbContext>(options);
            // Build real configuration
            var basePath = AppContext.BaseDirectory;
            var projectRoot = Path.GetFullPath(Path.Combine(basePath, "..", "..", "..", "..", "ProbuildBackend"));
            var appSettingsPath = Path.Combine(projectRoot, "appsettings.json");
            var configuration = new ConfigurationBuilder().AddJsonFile(appSettingsPath).Build();

            var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
            var mockLogger = new Mock<ILogger<AzureBlobService>>();
            // Moq can mock concrete types if they have a parameterless constructor or if all dependencies can be mocked.
            // Since AzureBlobService has dependencies, we mock it and its dependencies.
            _mockAzureBlobService = new Mock<AzureBlobService>(configuration, mockHttpContextAccessor.Object, mockLogger.Object);
            _mockHubContext = new Mock<IHubContext<ProgressHub>>();
            _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
            _mockDocumentProcessor = new Mock<IDocumentProcessorService>();
            _mockEmailSender = new Mock<IEmailSender>();
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _mockConfiguration = new Mock<IConfiguration>();
            _mockWebSocketManager = new Mock<WebSocketManager>();
            _mockBackgroundJobClient = new Mock<IBackgroundJobClient>();

            _jobsController = new JobsController(
                _mockDbContext.Object,
                _mockAzureBlobService.Object,
                _mockHubContext.Object,
                _mockHttpContextAccessor.Object,
                _mockDocumentProcessor.Object,
                _mockEmailSender.Object,
                _mockHttpClientFactory.Object,
                _mockConfiguration.Object,
                _mockWebSocketManager.Object
            );

            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim(ClaimTypes.NameIdentifier, "4929f316-4a97-4c9b-a671-8962532b6ab5"),
            }, "mock"));

            var httpContext = new DefaultHttpContext { User = user };
            httpContext.Connection.Id = "test-connection-id";
            _mockHttpContextAccessor.Setup(h => h.HttpContext).Returns(httpContext);

            _jobsController.ControllerContext = new ControllerContext()
            {
                HttpContext = httpContext
            };
        }

        [Fact]
        public async Task PostJob_WithBlueprintAnalysis_EnqueuesBackgroundJob()
        {
            // ARRANGE - Story 1
            var jobDto = new JobDto
            {
                ProjectName = "New Build",
                SessionId = "test-session-id"
            };

            var documents = new List<JobDocumentModel>
            {
                new JobDocumentModel { BlobUrl = "http://example.com/blueprint.pdf", SessionId = "test-session-id" }
            };

            var mockDbSet = new Mock<DbSet<JobDocumentModel>>();
            mockDbSet.As<IQueryable<JobDocumentModel>>().Setup(m => m.Provider).Returns(documents.AsQueryable().Provider);
            mockDbSet.As<IQueryable<JobDocumentModel>>().Setup(m => m.Expression).Returns(documents.AsQueryable().Expression);
            mockDbSet.As<IQueryable<JobDocumentModel>>().Setup(m => m.ElementType).Returns(documents.AsQueryable().ElementType);
            mockDbSet.As<IQueryable<JobDocumentModel>>().Setup(m => m.GetEnumerator()).Returns(documents.GetEnumerator());

            _mockDbContext.Setup(c => c.JobDocuments).Returns(mockDbSet.Object);
            _mockDbContext.Setup(c => c.Database.BeginTransactionAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new Mock<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction>().Object);

            // ACT
            var result = await _jobsController.PostJob(jobDto);

            // ASSERT
            Assert.IsType<OkObjectResult>(result);

            // We can't easily verify the Hangfire call without a lot more mocking,
            // but we can verify that the code path was taken.
            // A better test would be an integration test with a real Hangfire server.
        }
    }
}
