using GenerativeAI;
using GenerativeAI.Types;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Services;

public class GeminiAiService : IAiService
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IPromptManagerService _promptManager;
    private readonly GenerativeModel _generativeModel;
    private readonly ILogger<GeminiAiService> _logger;
     private readonly AzureBlobService _azureBlobService;

    private const int SUMMARIZATION_THRESHOLD_CHARS = 250000;

  public GeminiAiService(IConfiguration configuration, IConversationRepository conversationRepo, IPromptManagerService promptManager, ILogger<GeminiAiService> logger, AzureBlobService azureBlobService)
  {
    _conversationRepo = conversationRepo;
    _promptManager = promptManager;
    _logger = logger;
    _azureBlobService = azureBlobService;

#if (DEBUG)
    var apiKey = configuration["GoogleGeminiAPI:APIKey"];
#else
    var apiKey = Environment.GetEnvironmentVariable("GeminiAPIKey");
#endif

    var googleAI = new GoogleAi(apiKey);
    _generativeModel = googleAI.CreateGenerativeModel("gemini-2.5-pro");
    _generativeModel.UseGoogleSearch = true;
    }

    #region Conversational Method
    public async Task<(string response, string conversationId)> ContinueConversationAsync(
        string? conversationId, string userId, string userPrompt, IEnumerable<string>? documentUris, bool isAnalysis = false)
    {
        var conversation = await GetOrCreateConversation(conversationId, userId, userPrompt);
        conversationId = conversation.Id;

        if (!isAnalysis)
        {
            await CompactHistoryIfRequiredAsync(conversation);
        }
        var updatedConv = await _conversationRepo.GetConversationAsync(conversationId) ?? conversation;

        var history = await BuildHistoryAsync(updatedConv);

        var request = new GenerateContentRequest { Contents = history };

        var currentUserContent = new Content { Role = Roles.User };
        currentUserContent.AddText(userPrompt);

        var tempFilePaths = new List<string>();
        if (documentUris != null)
        {
            foreach (var fileUri in documentUris)
            {
                try
                {
                    var (fileBytes, mimeType) = await _azureBlobService.DownloadBlobAsBytesAsync(fileUri);
                    var base64String = Convert.ToBase64String(fileBytes);
                    currentUserContent.AddInlineData(base64String, mimeType);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download or add file for analysis: {FileUri}", fileUri);
                }
            }
        }

        request.Contents.Add(currentUserContent);

        try
        {
            _logger.LogInformation("Sending request to Gemini: {PartsCount}", request.Contents.Sum(c => c.Parts?.Count ?? 0));
            string modelResponseText = string.Empty;
            try
            {
                _logger.LogInformation("Calling Gemini with {PartCount} parts", request.Contents.Sum(c => c.Parts?.Count ?? 0));
                _logger.LogInformation("ContinueConversationAsync Memory before Gemini call: {MemoryMb} MB", GC.GetTotalMemory(false) / 1024 / 1024);
                _logger.LogInformation("ContinueConversationAsync GC Memory Info: {Info}", GC.GetGCMemoryInfo().ToString());
                _logger.LogInformation("ContinueConversationAsync");


                var response = await _generativeModel.GenerateContentAsync(request);
                modelResponseText = response.Text();
                _logger.LogInformation("Gemini returned response of length {Length}", modelResponseText.Length);

            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Gemini call crashed the app");
                throw;
            }

            // await _conversationRepo.AddMessageAsync(new Message { ConversationId = conversationId, Role = "user", Content = userPrompt });
            // await _conversationRepo.AddMessageAsync(new Message { ConversationId = conversationId, Role = "model", Content = modelResponseText });

            return (modelResponseText, conversationId);
          }
          catch (Exception ex)
          {
              _logger.LogError(ex, "An error occurred while calling the Gemini API in ContinueConversationAsync for conversation {ConversationId}", conversationId);
              throw; // Re-throw the exception to be handled by the caller
          }
          finally
          {
              foreach (var path in tempFilePaths)
              {
                  if (File.Exists(path)) File.Delete(path);
              }
          }
    }

    private async Task<Conversation> GetOrCreateConversation(string? id, string userId, string title)
    {
        if (!string.IsNullOrEmpty(id))
        {
            _logger.LogInformation("Continuing conversation {ConversationId}", id);
            return await _conversationRepo.GetConversationAsync(id) ?? throw new KeyNotFoundException("Conversation not found.");
        }
        _logger.LogInformation("Creating new conversation for user {UserId}", userId);
        var newId = await _conversationRepo.CreateConversationAsync(userId, title.Substring(0, Math.Min(title.Length, 50)), null);
        return await _conversationRepo.GetConversationAsync(newId) ?? throw new Exception("Failed to create or retrieve conversation.");
    }

    private async Task<List<Content>> BuildHistoryAsync(Conversation conv)
    {
        var history = new List<Content>();

        // For prompt-based conversations, fetch and add the correct system prompt.
        if (conv.PromptKeys != null && conv.PromptKeys.Any() && history.Count == 0)
        {
            if (conv.PromptKeys.Any(p => p.PromptKey == "SYSTEM_RENOVATION_ANALYSIS"))
            {
                var renovationPrompt = await _promptManager.GetPromptAsync("", "renovation-persona.txt");
                history.Add(new Content { Role = Roles.User, Parts = new List<Part> { new Part { Text = renovationPrompt } } });
                history.Add(new Content { Role = Roles.Model, Parts = new List<Part> { new Part { Text = "Understood. I will act as a construction Project Manager, Quantity Surveyor and Financial Advisor with specialized expertise in renovation and restoration projects. I am ready to begin." } } });
            }
            else
            {
                var systemPrompt = await _promptManager.GetPromptAsync("", "system-persona.txt");
                history.Add(new Content { Role = Roles.User, Parts = new List<Part> { new Part { Text = systemPrompt } } });
                history.Add(new Content { Role = Roles.Model, Parts = new List<Part> { new Part { Text = "Understood. I will act as a construction Project Manager, Quantity Surveyor and Financial Advisor. I am ready to begin." } } });
            }
        }

        // Add conversation summary if it exists.
        if (!string.IsNullOrWhiteSpace(conv.ConversationSummary))
        {
            history.Add(new Content { Role = Roles.User, Parts = new List<Part> { new Part { Text = $"**Summary of the conversation so far:**\n{conv.ConversationSummary}" } } });
            history.Add(new Content { Role = Roles.Model, Parts = new List<Part> { new Part { Text = "Okay, I have reviewed the summary. I am ready to continue." } } });
        }

        // Add the rest of the message history.
        // For text-only chats, the history starts here.
        var recentMessages = await _conversationRepo.GetUnsummarizedMessagesAsync(conv.Id);
        foreach (var message in recentMessages)
        {
            var apiRole = message.Role.ToLower() == "assistant" ? "model" : "user";
            history.Add(new Content { Role = apiRole, Parts = new List<Part> { new Part { Text = message.Content } } });
        }

        return history;
    }

    private async Task CompactHistoryIfRequiredAsync(Conversation conversation)
    {
        var unsummarized = await _conversationRepo.GetUnsummarizedMessagesAsync(conversation.Id);
        int charCount = unsummarized.Sum(m => m.Content.Length);

        if (charCount < SUMMARIZATION_THRESHOLD_CHARS) return;

        _logger.LogInformation("Compacting history for conversation {ConversationId}, charCount: {CharCount}", conversation.Id, charCount);
        string historyToSummarize = string.Join("\n", unsummarized.Select(m => $"{m.Role}: {m.Content}"));
        var summarizationPrompt = $@"The following is a segment of a long construction analysis conversation. Create a concise summary of this segment. Focus on extracting key facts, decisions, user requirements, and file references. This summary will be combined with previous summaries to provide long-term context.
Previous Summary (for context):
{conversation.ConversationSummary ?? "N/A"}
New Segment to Summarize:
{historyToSummarize}

New, Updated, and Consolidated Summary:";

        try
        {
            _logger.LogInformation("CompactHistoryIfRequiredAsync Memory before Gemini call: {MemoryMb} MB", GC.GetTotalMemory(false) / 1024 / 1024);
            _logger.LogInformation("CompactHistoryIfRequiredAsync GC Memory Info: {Info}", GC.GetGCMemoryInfo().ToString());
            _logger.LogInformation("CompactHistoryIfRequiredAsync");
            var response = await _generativeModel.GenerateContentAsync(summarizationPrompt);
            var summaryResponseText = response.Text();

            await _conversationRepo.UpdateConversationSummaryAsync(conversation.Id, summaryResponseText);
            await _conversationRepo.MarkMessagesAsSummarizedAsync(unsummarized.Select(m => m.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while compacting history for conversation {ConversationId}", conversation.Id);
            // Don't re-throw here as summarization failure is not critical to the main flow.
        }
    }
    #endregion

    #region Other Implemented Interface Methods
    public async Task<string> AnalyzePageWithAssistantAsync(byte[] imageBytes, int pageIndex, string blobUrl, JobModel job)
    {
        var userPrompt = $@"Page {pageIndex + 1} of a construction document. Please analyze the architectural drawing and provide a detailed construction analysis in markdown format with:
Building Description, Layout & Design, Materials List (with estimated quantities), Cost Estimate, Other Notes (legends, symbols, dimensions)
Use the following details as a cheat sheet for assumptions and guidance:
Start Date: {job.DesiredStartDate:yyyy-MM-dd}, Wall Structure: {job.WallStructure}, Wall Insulation: {job.WallInsulation}, Roof Structure: {job.RoofStructure}, Roof Insulation: {job.RoofInsulation}, Foundation: {job.Foundation}, Finishes: {job.Finishes}, Electrical Supply Needs: {job.ElectricalSupplyNeeds}, Number of Stories: {job.Stories}, Building Size: {job.BuildingSize} sq ft";

        var request = new GenerateContentRequest();
        var content = new Content { Role = Roles.User };
        content.AddText(userPrompt);

        var (mimeType, extension) = MimeTypeValidator.GetMimeType(imageBytes);
        var tempFilePath = Path.GetTempFileName() + extension;
        await File.WriteAllBytesAsync(tempFilePath, imageBytes);
        content.AddInlineFile(tempFilePath, mimeType);
        request.Contents = new List<Content> { content };

        try
        {
            _logger.LogInformation("AnalyzePageWithAssistantAsync Memory before Gemini call: {MemoryMb} MB", GC.GetTotalMemory(false) / 1024 / 1024);
            _logger.LogInformation("AnalyzePageWithAssistantAsync GC Memory Info: {Info}", GC.GetGCMemoryInfo().ToString());
            _logger.LogInformation("AnalyzePageWithAssistantAsync");
            var response = await _generativeModel.GenerateContentAsync(request);
            return response.Text();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while calling the Gemini API in AnalyzePageWithAssistantAsync.");
            throw;
        }
        finally
        {
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
        }
    }

    public async Task<string> RefineTextWithAiAsync(string extractedText, string blobUrl)
    {
        _logger.LogInformation("Refining text for blob(s): {BlobUrl}. Text length: {TextLength}", blobUrl, extractedText.Length);
        var prompt = $@"You are a document processing expert. The following text was extracted from a construction document. Your task is to review, clean up, and structure this text into a clear and coherent summary. Correct any OCR errors, format it logically using markdown, and synthesize the information into a professional report.
Extracted Text:
{extractedText}
Refined Output:";
        try
        {
            _logger.LogInformation("RefineTextWithAiAsync Memory before Gemini call: {MemoryMb} MB", GC.GetTotalMemory(false) / 1024 / 1024);
            _logger.LogInformation("RefineTextWithAiAsync GC Memory Info: {Info}", GC.GetGCMemoryInfo().ToString());
            _logger.LogInformation("RefineTextWithAiAsync");
            var response = await _generativeModel.GenerateContentAsync(prompt);
            var refinedText = response.Text();
            _logger.LogInformation("Successfully refined text for blob(s): {BlobUrl}. Refined text length: {RefinedTextLength}", blobUrl, refinedText.Length);
            return refinedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while calling the Gemini API in RefineTextWithAiAsync for blob(s): {BlobUrl}", blobUrl);
            throw;
        }
    }

    public async Task<BillOfMaterials> GenerateBomFromText(string documentText)
    {
        var prompt = $@"You are a construction document parser specializing in generating a Bill of Materials (BOM). Analyze the following text extracted from a construction plan. Extract all materials, estimate their quantities, and provide the output in a valid JSON format. The JSON object should have a single key 'BillOfMaterialsItems' which is an array of objects. Each object in the array should have three string properties: 'Item', 'Description', and 'Quantity'.
Document Text:
{documentText}
JSON Output:";

        try
        {
            var bom = await _generativeModel.GenerateObjectAsync<BillOfMaterials>(prompt);
            return bom ?? new BillOfMaterials { BillOfMaterialsItems = new List<BomItem>() };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to deserialize BOM from Gemini. Error: {ex.Message}");
            return new BillOfMaterials { BillOfMaterialsItems = new List<BomItem>() };
        }
    }

    public async Task<string> PerformMultimodalAnalysisAsync(IEnumerable<string> fileUris, string prompt, bool isAnalysis = false)
    {
        _logger.LogInformation("Performing multimodal analysis with {FileCount} files.", fileUris.Count());
        var userContent = new Content { Role = Roles.User };
        userContent.AddText(prompt);

        foreach (var fileUri in fileUris)
        {
            try
            {
                var (fileBytes, mimeType) = await _azureBlobService.DownloadBlobAsBytesAsync(fileUri);
                var base64String = Convert.ToBase64String(fileBytes);

                userContent.AddInlineData(base64String, mimeType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download or add file for analysis: {FileUri}", fileUri);
            }
        }

        var request = new GenerateContentRequest
        {
            Contents = new List<Content> { userContent }
        };

        try
        {
            var response = await _generativeModel.GenerateContentAsync(request);
            return response.Text();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while calling the Gemini API in PerformMultimodalAnalysisAsync.");
            throw;
        }
    }
    #endregion

    public async Task<(string initialResponse, string conversationId)> StartMultimodalConversationAsync(string userId, IEnumerable<string> documentUris, string systemPersonaPrompt, string initialUserPrompt, string? conversationId = null)
    {
        _logger.LogInformation("START: StartMultimodalConversationAsync for User {UserId}", userId);

        // 1. Create a new conversation
        var conversationTitle = $"Analysis started on {DateTime.UtcNow:yyyy-MM-dd}";
        _logger.LogInformation("Creating conversation with title: {Title}", conversationTitle);
        if (string.IsNullOrEmpty(conversationId))
        {
            conversationId = await _conversationRepo.CreateConversationAsync(userId, conversationTitle, new List<string> { "system-persona.txt" });
        }
        var conversation = await _conversationRepo.GetConversationAsync(conversationId) ?? throw new Exception("Failed to create or retrieve conversation.");
        _logger.LogInformation("Conversation {ConversationId} created.", conversationId);

        // 2. Construct the initial request
        var systemContent = new Content { Role = Roles.User, Parts = new List<Part> { new Part { Text = systemPersonaPrompt } } };
        var modelResponseToSystem = new Content { Role = Roles.Model, Parts = new List<Part> { new Part { Text = "Understood. I will act as a construction Project Manager, Quantity Surveyor and Financial Advisor. I am ready to begin." } } };

        var userContent = new Content { Role = Roles.User };
        userContent.AddText(initialUserPrompt);

        _logger.LogInformation("Processing {DocumentCount} document URIs.", documentUris.Count());
        foreach (var fileUri in documentUris)
        {
            try
            {
                _logger.LogInformation("Downloading blob: {FileUri}", fileUri);
                var (fileBytes, mimeType) = await _azureBlobService.DownloadBlobAsBytesAsync(fileUri);
                var base64String = Convert.ToBase64String(fileBytes);
                userContent.AddInlineData(base64String, mimeType);
                _logger.LogInformation("Added file to request: {FileUri}, MimeType: {MimeType}, Size: {Size} bytes", fileUri, mimeType, fileBytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download or add file for analysis: {FileUri}", fileUri);
            }
        }

        var request = new GenerateContentRequest
        {
            Contents = new List<Content> { systemContent, modelResponseToSystem, userContent }
        };

        try
        {
            // 3. Send the request
            _logger.LogInformation("Sending request to Gemini: {PartsCount} parts", request.Contents.Sum(c => c.Parts?.Count ?? 0));
            string modelResponseText = string.Empty;
            try
            {
                _logger.LogInformation("Calling Gemini with {PartCount} parts", request.Contents.Sum(c => c.Parts?.Count ?? 0));
                _logger.LogInformation("StartMultimodalConversationAsync Memory before Gemini call: {MemoryMb} MB", GC.GetTotalMemory(false) / 1024 / 1024);
                _logger.LogInformation("StartMultimodalConversationAsync GC Memory Info: {Info}", GC.GetGCMemoryInfo().ToString());
                _logger.LogInformation("StartMultimodalConversationAsync");
                var response = await _generativeModel.GenerateContentAsync(request);
                 modelResponseText = response.Text();
                _logger.LogInformation("Gemini returned response of length {Length}", modelResponseText.Length);

            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Gemini call crashed the app");
                throw;
            }

            // 4. Store initial messages
            _logger.LogInformation("Storing initial messages for conversation {ConversationId}", conversationId);
            if (!string.IsNullOrWhiteSpace(initialUserPrompt))
            {
                await _conversationRepo.AddMessageAsync(new Message { ConversationId = conversationId, Role = "user", Content = initialUserPrompt });
            }
            await _conversationRepo.AddMessageAsync(new Message { ConversationId = conversationId, Role = "model", Content = modelResponseText });

            // 5. Return response and ID
            _logger.LogInformation("Successfully started multimodal conversation {ConversationId}", conversationId);
            return (modelResponseText, conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EXCEPTION in StartMultimodalConversationAsync for conversation {ConversationId}", conversationId);
            throw;
        }
        finally
        {
            _logger.LogInformation("END: StartMultimodalConversationAsync for User {UserId}", userId);
        }
    }

    public async Task<(string response, string conversationId)> StartTextConversationAsync(string userId, string systemPersonaPrompt, string initialUserPrompt, string? conversationId = null)
    {
        _logger.LogInformation("Starting new text-only conversation for user {UserId}", userId);

        // 1. Create a new conversation
        if (string.IsNullOrEmpty(conversationId))
        {
            var conversationTitle = $"Chat started on {DateTime.UtcNow:yyyy-MM-dd}";
            conversationId = await _conversationRepo.CreateConversationAsync(userId, conversationTitle, new List<string> { "system-persona.txt" });
        }

        // 2. Construct the initial request
        var systemContent = new Content { Role = Roles.User, Parts = new List<Part> { new Part { Text = systemPersonaPrompt } } };
        var modelResponseToSystem = new Content { Role = Roles.Model, Parts = new List<Part> { new Part { Text = "Understood. I am ready to assist." } } };
        var userContent = new Content { Role = Roles.User };
        userContent.AddText(initialUserPrompt);

        var request = new GenerateContentRequest
        {
            Contents = new List<Content> { systemContent, modelResponseToSystem, userContent }
        };

        try
        {
            // 3. Send the request
            var response = await _generativeModel.GenerateContentAsync(request);
            var modelResponseText = response.Text();

            // 4. Store initial messages
            await _conversationRepo.AddMessageAsync(new Message { ConversationId = conversationId, Role = "user", Content = initialUserPrompt });
            await _conversationRepo.AddMessageAsync(new Message { ConversationId = conversationId, Role = "model", Content = modelResponseText });

            // 5. Return response and ID
            _logger.LogInformation("Successfully started text-only conversation {ConversationId}", conversationId);
            return (modelResponseText, conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while calling the Gemini API in StartTextConversationAsync for conversation {ConversationId}", conversationId);
            throw;
        }
    }

    private static bool LogAndReturnFalse(Exception ex)
    {
        Console.WriteLine($"[Critical Gemini Crash] {ex}");
        return false;
    }
}
