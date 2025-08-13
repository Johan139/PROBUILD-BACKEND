using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProbuildBackend.Interface;
using ProbuildBackend.Services;

namespace ProbuildBackend.Tests.Integration
{
    public class CustomWebApplicationFactory<TStartup> : WebApplicationFactory<TStartup> where TStartup : class
    {
        public Mock<IConversationRepository> MockConversationRepo { get; } = new Mock<IConversationRepository>();
        public Mock<IAnalysisService> MockAnalysisService { get; } = new Mock<IAnalysisService>();
        public Mock<IAiService> MockAiService { get; } = new Mock<IAiService>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing registrations if they exist
                var conversationRepoDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IConversationRepository));
                if (conversationRepoDescriptor != null) services.Remove(conversationRepoDescriptor);

                var analysisServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAnalysisService));
                if (analysisServiceDescriptor != null) services.Remove(analysisServiceDescriptor);

                var aiServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAiService));
                if (aiServiceDescriptor != null) services.Remove(aiServiceDescriptor);

                // Add mock services
                services.AddSingleton(MockConversationRepo.Object);
                services.AddSingleton(MockAnalysisService.Object);
                services.AddSingleton(MockAiService.Object);
            });
        }
    }
}