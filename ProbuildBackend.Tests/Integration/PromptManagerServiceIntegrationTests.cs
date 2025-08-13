using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace ProbuildBackend.Tests.Services
{
    public class PromptManagerServiceIntegrationTests
    {
        private readonly IConfiguration _configuration;
        private readonly PromptManagerService _service;

        public PromptManagerServiceIntegrationTests()
        {
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

            _service = new PromptManagerService(_configuration);
        }

        public static IEnumerable<object[]> GetAllPromptFileNames()
        {
            var jsonPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ProbuildBackend", "Config", "prompt_mapping.json");
            var json = File.ReadAllText(jsonPath);
            var prompts = JsonSerializer.Deserialize<List<PromptMapping>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            // Handle the case where deserialization might return null.
            var allPrompts = new HashSet<string>();
            if (prompts != null)
            {
                allPrompts = new HashSet<string>(prompts.Where(p => !string.IsNullOrEmpty(p.PromptFileName)).Select(p => p.PromptFileName!));
            }

            // Add system-level prompts
            allPrompts.Add("system-persona.txt");
            allPrompts.Add("prompt-00-initial-analysis.txt");
            allPrompts.Add("prompt-failure-corrective-action.txt");
            allPrompts.Add("prompt-22-rebuttal.txt");
            allPrompts.Add("prompt-revision.txt");
            allPrompts.Add("generic-prompt.txt");


            // Add all sequential prompts from the ComprehensiveAnalysisService
            var sequentialPrompts = new[] {
                "prompt-01-sitelogistics.txt", "prompt-02-groundwork.txt", "prompt-03-framing.txt",
                "prompt-04-roofing.txt", "prompt-05-exterior.txt", "prompt-06-electrical.txt",
                "prompt-07-plumbing.txt", "prompt-08-hvac.txt", "prompt-09-insulation.txt",
                "prompt-10-drywall.txt", "prompt-11-painting.txt", "prompt-12-trim.txt",
                "prompt-13-kitchenbath.txt", "prompt-14-flooring.txt", "prompt-15-exteriorflatwork.txt",
                "prompt-16-cleaning.txt", "prompt-17-costbreakdowns.txt", "prompt-18-riskanalyst.txt",
                "prompt-19-timeline.txt", "prompt-20-environmental.txt", "prompt-21-closeout.txt"
            };
            foreach (var p in sequentialPrompts)
            {
                allPrompts.Add(p);
            }

            return allPrompts.Select(p => new object[] { p });
        }

        [Theory]
        [MemberData(nameof(GetAllPromptFileNames))]
        public async Task GetPrompt_ReturnsContent_ForEveryMappedPrompt(string promptFileName)
        {
            // Arrange
            string userType = ""; // Service logic determines path based on filename for these prompts

            // Act
            var promptText = await _service.GetPromptAsync(userType, promptFileName);

            // Assert
            Assert.NotNull(promptText);
            Assert.False(string.IsNullOrWhiteSpace(promptText), $"Prompt '{promptFileName}' should not be empty.");
        }
    }

    public class PromptMapping
    {
        public string? TradeName { get; set; }
        public string? PromptFileName { get; set; }
        public string? DisplayName { get; set; }
    }
}
