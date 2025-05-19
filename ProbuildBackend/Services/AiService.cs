using OpenAI;
using OpenAI.Chat;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Options;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using static ProbuildBackend.Services.DocumentProcessorService;

namespace ProbuildBackend.Services
{
    public class AiService : IAiService
    {
        private readonly ChatClient _chatClient;
        private readonly ChatClient _chatClient3Turbo;
        private readonly OcrSettings _settings;
        private readonly HttpClient _openAiHttpClient;
        private const string AssistantId = "asst_avihQboQwQCI6dz0gukIMrHD";

        public AiService(OcrSettings settings, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {

            _settings = new OcrSettings
            {
                Dpi = 150,
                MaxImageWidth = 2048,
                MaxImageHeight = 2048,
                MaxConcurrentPages = 2,
                ThrottleDelayMs = 3500,
                MaxTokens = 4000
            };


            string apiKey = Environment.GetEnvironmentVariable("GPTAPIKEY")
          ?? configuration["ChatGPTAPI:APIKey"];

            var openAIClient = new OpenAIClient(apiKey);
            var openAIClient3 = new OpenAIClient(apiKey);
            _chatClient = openAIClient.GetChatClient("gpt-3.5-turbo");
            _chatClient3Turbo = openAIClient3.GetChatClient("gpt-4o-mini");

            _openAiHttpClient = httpClientFactory.CreateClient("OpenAI");
            _openAiHttpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
            _openAiHttpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
        public async Task<string> AnalyzePageWithAssistantAsync(byte[] imageBytes, int pageIndex, string blobUrl, JobModel job)
        {
            // Step 1: Upload the image to OpenAI
            Console.WriteLine("Uploading image...");
            using var uploadContent = new MultipartFormDataContent();
            uploadContent.Add(new StringContent("assistants"), "purpose");

            var imageFileContent = new ByteArrayContent(imageBytes);
            imageFileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            uploadContent.Add(imageFileContent, "file", $"page-{pageIndex + 1}.png");

            var uploadResponse = await _openAiHttpClient.PostAsync("files", uploadContent);
            var uploadJson = await uploadResponse.Content.ReadAsStringAsync();

            Console.WriteLine("Upload Response:");
            Console.WriteLine(uploadJson);

            if (!uploadResponse.IsSuccessStatusCode)
                throw new Exception($"Image upload failed: {uploadResponse.StatusCode} - {uploadJson}");

            var fileId = JsonDocument.Parse(uploadJson).RootElement.GetProperty("id").GetString();
            Console.WriteLine("✅ Uploaded File ID: " + fileId);

            // Step 2: Create thread
            var threadRequest = new HttpRequestMessage(HttpMethod.Post, "threads")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            threadRequest.Headers.Add("OpenAI-Beta", "assistants=v2");

            var threadResponse = await _openAiHttpClient.SendAsync(threadRequest);
            var threadJson = await threadResponse.Content.ReadAsStringAsync();
            Console.WriteLine("Thread Response:");
            Console.WriteLine(threadJson);

            if (!threadResponse.IsSuccessStatusCode)
                throw new Exception($"Thread creation failed: {threadResponse.StatusCode} - {threadJson}");

            var threadId = JsonDocument.Parse(threadJson).RootElement.GetProperty("id").GetString();

            // Step 3: Send message with file reference
            var message = new
            {
                role = "user",
                content = new object[]
                {
        new { type = "text", text = $@"
Page {pageIndex + 1} of a construction document. Please analyze the architectural drawing and provide a detailed construction analysis in markdown format with:
- Building Description
- Layout & Design
- Materials List (with estimated quantities)
- Cost Estimate
- Other Notes (legends, symbols, dimensions)
- USE THIS DATE FOR START DATE AND CALCULATE ENDDATE OF IT{job.DesiredStartDate.ToString()}" },

        new
        {
            type = "image_file",
            image_file = new { file_id = fileId }
        }
                }
            };

            var msgRequest = new HttpRequestMessage(HttpMethod.Post, $"threads/{threadId}/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json")
            };
            msgRequest.Headers.Add("OpenAI-Beta", "assistants=v2");

            Console.WriteLine("Sending message payload:");
            Console.WriteLine(JsonSerializer.Serialize(message, new JsonSerializerOptions { WriteIndented = true }));

            var msgResponse = await _openAiHttpClient.SendAsync(msgRequest);
            var msgJson = await msgResponse.Content.ReadAsStringAsync();
            Console.WriteLine("Message Response:");
            Console.WriteLine(msgJson);

            if (!msgResponse.IsSuccessStatusCode)
                throw new Exception($"Message creation failed: {msgResponse.StatusCode} - {msgJson}");

            // Step 4: Run the assistant
            var runRequest = new HttpRequestMessage(HttpMethod.Post, $"threads/{threadId}/runs")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { assistant_id = AssistantId }), Encoding.UTF8, "application/json")
            };
            runRequest.Headers.Add("OpenAI-Beta", "assistants=v2");

            var runResponse = await _openAiHttpClient.SendAsync(runRequest);
            var runJson = await runResponse.Content.ReadAsStringAsync();
            Console.WriteLine("Run Response:");
            Console.WriteLine(runJson);

            if (!runResponse.IsSuccessStatusCode)
                throw new Exception($"Run failed: {runResponse.StatusCode} - {runJson}");

            var runId = JsonDocument.Parse(runJson).RootElement.GetProperty("id").GetString();

            // Step 5: Poll for completion
            string status;
            do
            {
                await Task.Delay(1500);
                var pollRequest = new HttpRequestMessage(HttpMethod.Get, $"threads/{threadId}/runs/{runId}");
                pollRequest.Headers.Add("OpenAI-Beta", "assistants=v2");

                var pollResponse = await _openAiHttpClient.SendAsync(pollRequest);
                var pollJson = await pollResponse.Content.ReadAsStringAsync();
                status = JsonDocument.Parse(pollJson).RootElement.GetProperty("status").GetString();

                Console.WriteLine($"Run status: {status}");
            } while (status == "queued" || status == "in_progress");

            // Step 6: Retrieve assistant response
            var fetchRequest = new HttpRequestMessage(HttpMethod.Get, $"threads/{threadId}/messages");
            fetchRequest.Headers.Add("OpenAI-Beta", "assistants=v2");

            var fetchResponse = await _openAiHttpClient.SendAsync(fetchRequest);
            var fetchJson = await fetchResponse.Content.ReadAsStringAsync();

            Console.WriteLine("Final message response:");
            Console.WriteLine(fetchJson);

            if (!fetchResponse.IsSuccessStatusCode)
                throw new Exception($"Fetch failed: {fetchResponse.StatusCode} - {fetchJson}");

            var msgDoc = JsonDocument.Parse(fetchJson);
            var contentText = msgDoc.RootElement
                .GetProperty("data")[0]
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetProperty("value")
                .GetString();

            return contentText;
        }


        public async Task<string> AnalyzePageWithAiAsync(byte[] imageBytes, int pageIndex, string blobUrl)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                var messages = new List<ChatMessage>
        {
            new SystemChatMessage(LoadPromptTemplate("analysis")),
           new UserChatMessage(new[]
                    {
                        ChatMessageContentPart.CreateTextPart($@"
                        Page {pageIndex + 1} of a building plan document. Analyze the image and provide a detailed report in markdown format with the following sections:
                        - **Building Description**
                        - **Layout & Design**
                        - **Materials List with rough estimate of quantities if you cannot figure out the quantity, just return somthing that seems about right**
                        - **Cost Estimate**
                        - **Other Notes (legends, symbols, dimensions, etc.)**
                        Ensure the output is structured, detailed, and consistent with professional construction analysis standards."),

                        ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), "image/png", ChatImageDetailLevel.High)
                    })
                };

            var chatOptions = new ChatCompletionOptions { MaxOutputTokenCount = _settings.MaxTokens };
            ChatCompletion response = await _chatClient3Turbo.CompleteChatAsync(messages, chatOptions);

            return response.Content?.FirstOrDefault()?.Text ?? $"[Page {pageIndex + 1}: No text extracted]";
        });
        }
        public async Task<string> RefineTextWithAiAsync(string extractedText, string blobUrl)
        {
            var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a senior construction documentation expert tasked with refining raw construction analysis from a multi-page plan document into a cohesive, professional report. Your output must be highly detailed, technically precise, and comprehensive, matching the depth of a human expert’s analysis.\r\n\r\n---\r\n\r\n## 🔧 FORMAT ENFORCEMENT INSTRUCTIONS\r\n\r\nYou must return your response using the **exact markdown structure** shown below.  \r\n- **Every section is required** and must be returned even if values are placeholders.  \r\n- Output must begin with `# Building Plan Analysis Report`.  \r\n- The section `## **Construction Timeline by Task Category**` must follow this structure exactly:\r\n  - Each task must be formatted as a **`### # X. Task Name (MasterFormat Code)`** section.\r\n  - Each subtask must appear **bolded**, with a block underneath showing:\r\n    - **Duration**\r\n    - **Start Date**\r\n    - **End Date**\r\n  - Each subtask block must end with a horizontal separator: `---`.\r\n\r\n**Do not skip or rename any of the following required sections**:\r\n\r\n```\r\n# Building Plan Analysis Report\r\n\r\n## **Building Description**\r\n- **Type:** \r\n- **Design Characteristics:** \r\n\r\n## **Layout & Design**\r\n- **Rooms Identified:** \r\n- **Access Points:** \r\n- **Vertical Circulation:** \r\n- **Unique Features:** \r\n\r\n## **Materials List**\r\n| Item                 | Quantity | Unit      | Location/Notes                          |\r\n|----------------------|----------|-----------|-----------------------------------------|\r\n|                      |          |           |                                         |\r\n\r\n## **Construction Timeline by Task Category**\r\n\r\n### # X. [Your Task Name Here] (MasterFormat Code)\r\n**Duration:** \r\n**Start Date:** \r\n**End Date:** \r\n\r\n**[Subtask Name]**  \r\n**Duration:**   \r\n**Start Date:**   \r\n**End Date:**   \r\n---\r\n\r\n(repeat as needed)\r\n\r\n## **Final Bill of Materials**\r\n| Item                 | Quantity | Unit      | Justification                          |\r\n|----------------------|----------|-----------|----------------------------------------|\r\n|                      |          |           |                                        |\r\n\r\nThis report consolidates the construction analysis based on the provided materials and inferred tasks, ensuring a comprehensive overview for project planning and execution.\r\n```\r\n\r\n---\r\n\r\n## 🎯 REFINEMENT TASKS\r\n\r\n1. **Merge Redundant Data**: Consolidate repeated material entries or sections across multiple pages into a single, unified set. Example: merge duplicate mentions of 'framing lumber' into one entry with a clear justification.\r\n\r\n2. **Resolve Inconsistencies**:\r\n   - Standardize all units (e.g., board feet for lumber, square feet for drywall).\r\n   - Normalize terminology (e.g., 'roofing shingles' → 'roofing material').\r\n   - Correct conflicting values using domain logic (e.g., square footage ranges).\r\n\r\n3. **Improve Structure**:\r\n   - Maintain clear headers as shown in the structure above.\r\n   - Use MasterFormat codes for all tasks and subtasks.\r\n   - Ensure all subtasks use bolded names and horizontal separators.\r\n\r\n4. **Enhance Clarity**:\r\n   - Use markdown tables and formatted sections.\r\n   - No bullet points in subtasks — use bold labels and `---`.\r\n   - Tasks should be clearly separated by hierarchy.\r\n\r\n5. **Generate Final Bill of Materials**:\r\n   - Provide a single, consolidated table with items and justifications.\r\n   - Categorize BOM under timeline task groupings, linked to MasterFormat.\r\n\r\n6. **Apply Accurate MasterFormat Codes**:\r\n   - Use authoritative codes from https://crmservice.csinet.org/widgets/masterformat/numbersandtitles.aspx.\r\n   - If unsure, use the closest relevant section and note your assumption.\r\n\r\n7. **Categorize by Tasks**:\r\n   - Convert each BOM item into a main task.\r\n   - Infer realistic subtasks (e.g., drywall → install, finish, paint).\r\n\r\n8. **Group Tasks Together**:\r\n   - Keep subtasks under their main task.\r\n   - Sequence dates and durations from **April 16, 2025**, ensuring all subtasks fit within the main task duration.\r\n\r\n---\r\n\r\n## ✅ FINAL REQUIREMENTS\r\n\r\n- Your response must begin with `# Building Plan Analysis Report`.\r\n- You must return the full section layout shown above.\r\n- Do not include commentary or raw content outside the report structure.\r\n- Be clear, professional, and assume typical construction norms when inferring missing data.\r\n- Return only the final, clean markdown report."),
            new UserChatMessage($"Here is the extract:\n```\n{extractedText}\n```")
        };

            var chatOptions = new ChatCompletionOptions { MaxOutputTokenCount = _settings.MaxTokens, Temperature = 0.2f, TopP = 0.9f };
            ChatCompletion response = await _chatClient3Turbo.CompleteChatAsync(messages, chatOptions);

            return response.Content?.FirstOrDefault()?.Text ?? extractedText;
        }
        public async Task<string> CallCustomAssistantAsync(string userPrompt)
        {
            // Step 1: Create thread
            var threadResponse = await _openAiHttpClient.PostAsync("threads", new StringContent("{}", Encoding.UTF8, "application/json"));
            var threadJson = await threadResponse.Content.ReadAsStringAsync();
            var threadId = JsonDocument.Parse(threadJson).RootElement.GetProperty("id").GetString();

            // Step 2: Add message
            var messagePayload = JsonSerializer.Serialize(new
            {
                role = "user",
                content = userPrompt
            });
            await _openAiHttpClient.PostAsync(
                $"threads/{threadId}/messages",
                new StringContent(messagePayload, Encoding.UTF8, "application/json"));

            // Step 3: Run assistant
            var runPayload = JsonSerializer.Serialize(new { assistant_id = AssistantId });
            var runResponse = await _openAiHttpClient.PostAsync(
                $"threads/{threadId}/runs",
                new StringContent(runPayload, Encoding.UTF8, "application/json"));
            var runJson = await runResponse.Content.ReadAsStringAsync();
            var runId = JsonDocument.Parse(runJson).RootElement.GetProperty("id").GetString();

            // Step 4: Poll run status
            string status;
            do
            {
                await Task.Delay(1500);
                var checkResponse = await _openAiHttpClient.GetAsync($"threads/{threadId}/runs/{runId}");
                var checkJson = await checkResponse.Content.ReadAsStringAsync();
                status = JsonDocument.Parse(checkJson).RootElement.GetProperty("status").GetString();
            } while (status == "queued" || status == "in_progress");

            // Step 5: Get assistant response
            var msgResponse = await _openAiHttpClient.GetAsync($"threads/{threadId}/messages");
            var msgJson = await msgResponse.Content.ReadAsStringAsync();
            var msgDoc = JsonDocument.Parse(msgJson);

            var content = msgDoc.RootElement
                .GetProperty("data")[0]
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetProperty("value")
                .GetString();

            return content;
        }
        public async Task<BillOfMaterials> GenerateBomFromText(string documentText)
        {
            var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a construction document parser specializing in building plans."),
            new UserChatMessage("Extract BOM from this text:\n" + documentText)
        };

            var chatOptions = new ChatCompletionOptions { MaxOutputTokenCount = 1000 };
            ChatCompletion response = await _chatClient.CompleteChatAsync(messages, chatOptions);

            var jsonResponse = response.Content.FirstOrDefault()?.Text;
            var bom = JsonSerializer.Deserialize<BillOfMaterials>(jsonResponse);
            return bom ?? new BillOfMaterials { BillOfMaterialsItems = new List<BomItem>() };
        }

        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, int maxRetries = 5)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    attempt++;
                    return await operation();
                }
                catch (Exception ex) when (ex.Message.Contains("Rate limit reached") && attempt <= maxRetries)
                {
                    // Parse the "try again in X ms" from the error message
                    string errorMsg = ex.Message;
                    int waitTimeMs = 1000; // Default to 1 second

                    try
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(errorMsg, @"Please try again in (\d+)ms");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int extractedWaitTime))
                        {
                            waitTimeMs = extractedWaitTime;
                        }
                    }
                    catch
                    {
                        // If parsing fails, use exponential backoff
                        waitTimeMs = (int)Math.Pow(2, attempt) * 1000;
                    }

                    // Add a small jitter to prevent all retries hitting at exactly the same time
                    waitTimeMs += new Random().Next(100, 500);

                    Console.WriteLine($"Rate limit hit, retrying in {waitTimeMs}ms (Attempt {attempt}/{maxRetries})");
                    await Task.Delay(waitTimeMs);
                }
            }
        }
        private static readonly ConcurrentDictionary<string, string> _promptCache = new();
        private string LoadPromptTemplate(string name)
        {
            return _promptCache.GetOrAdd(name, key =>
                File.ReadAllText(Path.Combine("PromptTemplates", key + ".md")));
        }
    }

}

