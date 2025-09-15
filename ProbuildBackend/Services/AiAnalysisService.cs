using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Models.Enums;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProbuildBackend.Services
{
    public class AiAnalysisService : IAiAnalysisService
    {
        private readonly ILogger<AiAnalysisService> _logger;
        private readonly IPromptManagerService _promptManager;
        private readonly IAiService _aiService;
        private readonly IConversationRepository _conversationRepo;
        private readonly ApplicationDbContext _context;
        private readonly IPdfTextExtractionService _pdfTextExtractionService;
        private readonly AzureBlobService _azureBlobService;

        private const string SelectedAnalysisPersonaKey = "sub-contractor-selected-prompt-master-prompt.txt";
        private const string RenovationAnalysisPersonaKey = "ProBuildAI_Renovation_Prompt.txt";
        private const string FailureCorrectiveActionKey = "prompt-failure-corrective-action.txt";

        public AiAnalysisService(
            ILogger<AiAnalysisService> logger,
            IPromptManagerService promptManager,
            IAiService aiService,
            IConversationRepository conversationRepo,
            ApplicationDbContext context,
            IPdfTextExtractionService pdfTextExtractionService,
            AzureBlobService azureBlobService)
        {
            _logger = logger;
            _promptManager = promptManager;
            _aiService = aiService;
            _conversationRepo = conversationRepo;
            _context = context;
            _pdfTextExtractionService = pdfTextExtractionService;
            _azureBlobService = azureBlobService;
        }

        public async Task<string> PerformSelectedAnalysisAsync(string userId, AnalysisRequestDto requestDto, bool generateDetailsWithAi, string? conversationId = null)
        {
            if (requestDto?.PromptKeys == null || !requestDto.PromptKeys.Any())
            {
                throw new ArgumentException("At least one prompt key must be provided.", nameof(requestDto.PromptKeys));
            }

            var job = await _context.Jobs.FindAsync(requestDto.JobId);
            var title = $"Selected Analysis for {job?.ProjectName ?? "Job ID " + requestDto.JobId}";

            if (string.IsNullOrEmpty(conversationId))
            {
                if (!string.IsNullOrEmpty(requestDto.ConversationId))
                {
                    conversationId = requestDto.ConversationId;
                }
                else
                {
                    conversationId = await _conversationRepo.CreateConversationAsync(userId, title, requestDto.PromptKeys);
                }
            }

            try
            {
                string personaPromptKey = SelectedAnalysisPersonaKey;
                _logger.LogInformation("Performing 'Selected' analysis with persona: {PersonaKey}", personaPromptKey);

                string personaPrompt = await _promptManager.GetPromptAsync(null, personaPromptKey);
                var userContext = await GetUserContextAsString(requestDto.UserContext, null);

                var (initialResponse, _) = await _aiService.StartMultimodalConversationAsync(userId, requestDto.DocumentUrls, personaPrompt, userContext, conversationId);

                if (initialResponse.Contains("BLUEPRINT FAILURE", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Initial analysis failed for prompts: {PromptKeys}. Triggering corrective action.", string.Join(", ", requestDto.PromptKeys));
                    initialResponse = await HandleFailureAsync(conversationId, userId, requestDto.DocumentUrls, initialResponse);
                }

                var reportBuilder = new StringBuilder();
                reportBuilder.Append(initialResponse);

                // Remove the JSON requirement from the persona for subsequent calls
                var personaWithoutJson = new Regex(@"CRITICAL OUTPUT REQUIREMENT:.*?\}", RegexOptions.Singleline).Replace(personaPrompt, "");

                foreach (var promptKey in requestDto.PromptKeys)
                {
                    var subPrompt = await _promptManager.GetPromptAsync(null, promptKey);
                    var (analysisResult, _) = await _aiService.ContinueConversationAsync(conversationId, userId, subPrompt, requestDto.DocumentUrls, true, personaWithoutJson);
                    var message = new Message { ConversationId = conversationId, Role = "model", Content = analysisResult, Timestamp = DateTime.UtcNow };
                    await _conversationRepo.AddMessageAsync(message);
                    reportBuilder.Append("\n\n---\n\n");
                    reportBuilder.Append(analysisResult);
                }

                // Extract and execute Timeline and Cost prompts
                var timelinePromptRegex = new Regex(@"2\. Timeline Prompt:.*?(?=3\. Cost Prompt:)", RegexOptions.Singleline);
                var timelineMatch = timelinePromptRegex.Match(personaPrompt);
                if (timelineMatch.Success)
                {
                    var timelinePrompt = timelineMatch.Value;
                    var (timelineResult, _) = await _aiService.ContinueConversationAsync(conversationId, userId, timelinePrompt, requestDto.DocumentUrls, true, personaWithoutJson);
                    reportBuilder.Append("\n\n---\n\n");
                    reportBuilder.Append(timelineResult);
                }

                var costPromptRegex = new Regex(@"3\. Cost Prompt:.*", RegexOptions.Singleline);
                var costMatch = costPromptRegex.Match(personaPrompt);
                if (costMatch.Success)
                {
                    var costPrompt = costMatch.Value;
                    var (costResult, _) = await _aiService.ContinueConversationAsync(conversationId, userId, costPrompt, requestDto.DocumentUrls, true, personaWithoutJson);
                    reportBuilder.Append("\n\n---\n\n");
                    reportBuilder.Append(costResult);
                }

                if (job != null && generateDetailsWithAi)
                {
                   await ParseAndSaveAiJobDetails(job.Id, reportBuilder.ToString());
                }

                _logger.LogInformation("Analysis completed successfully for prompts: {PromptKeys}", string.Join(", ", requestDto.PromptKeys));
                return reportBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during analysis for prompts: {PromptKeys}", string.Join(", ", requestDto.PromptKeys));
                throw;
            }
        }

        public async Task<string> PerformComprehensiveAnalysisAsync(string userId, IEnumerable<string> documentUris, JobModel jobDetails, bool generateDetailsWithAi, string userContextText, string userContextFileUrl, string promptKey = "prompt-00-initial-analysis.txt")
        {
            _logger.LogInformation("START: PerformComprehensiveAnalysisAsync for User {UserId}, Job {JobId}", userId, jobDetails.Id);

            try
            {
                _logger.LogInformation("Fetching system persona prompt.");
                var systemPersonaPrompt = await _promptManager.GetPromptAsync("", "system-persona.txt");
                _logger.LogInformation("Fetching initial analysis prompt: {PromptKey}", promptKey);
                var initialAnalysisPrompt = await _promptManager.GetPromptAsync("", promptKey);

                _logger.LogInformation("Getting user context as string.");
                var userContext = await GetUserContextAsString(userContextText, userContextFileUrl);

                var initialUserPrompt = $"{initialAnalysisPrompt}\n\n{userContext}\n\nHere are the project details:\n" +
                                        $"Project Name: {jobDetails.ProjectName}\n" +
                                        $"Job Type: {jobDetails.JobType}\n" +
                                        $"Address: {jobDetails.Address}\n" +
                                        $"Operating Area: {jobDetails.OperatingArea}\n" +
                                        $"Desired Start Date: {jobDetails.DesiredStartDate:yyyy-MM-dd}\n" +
                                        $"Stories: {jobDetails.Stories}\n" +
                                        $"Building Size: {jobDetails.BuildingSize} sq ft\n" +
                                        $"Wall Structure: {jobDetails.WallStructure}\n" +
                                        $"Wall Insulation: {jobDetails.WallInsulation}\n" +
                                        $"Roof Structure: {jobDetails.RoofStructure}\n" +
                                        $"Roof Insulation: {jobDetails.RoofInsulation}\n" +
                                        $"Foundation: {jobDetails.Foundation}\n" +
                                        $"Finishes: {jobDetails.Finishes}\n" +
                                        $"Electrical Needs: {jobDetails.ElectricalSupplyNeeds}";

                _logger.LogInformation("Initial user prompt created. Length: {Length}", initialUserPrompt.Length);

                _logger.LogInformation("Calling StartMultimodalConversationAsync.");
                var (initialResponse, conversationId) = await _aiService.StartMultimodalConversationAsync(userId, documentUris, systemPersonaPrompt, initialUserPrompt);
                _logger.LogInformation("Started multimodal conversation {ConversationId} for user {UserId}. Initial response length: {Length}", conversationId, userId, initialResponse?.Length ?? 0);

                if (initialResponse.Contains("BLUEPRINT FAILURE", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Initial analysis failed for conversation {ConversationId}. Triggering corrective action.", conversationId);
                    return await HandleFailureAsync(conversationId, userId, documentUris, initialResponse);
                }

                _logger.LogInformation("Blueprint fitness check PASSED for conversation {ConversationId}. Proceeding with full sequential analysis.", conversationId);

                if (generateDetailsWithAi)
                {
                   _logger.LogInformation("Parsing and saving AI job details for Job {JobId}", jobDetails.Id);
                   await ParseAndSaveAiJobDetails(jobDetails.Id, initialResponse);
                }

                _logger.LogInformation("Executing sequential prompts for conversation {ConversationId}", conversationId);
                return await ExecuteSequentialPromptsAsync(conversationId, userId, initialResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXCEPTION in PerformComprehensiveAnalysisAsync for User {UserId}, Job {JobId}", userId, jobDetails.Id);
                throw;
            }
            finally
            {
                _logger.LogInformation("END: PerformComprehensiveAnalysisAsync for User {UserId}, Job {JobId}", userId, jobDetails.Id);
            }
        }

        public async Task<string> PerformRenovationAnalysisAsync(string userId, IEnumerable<string> documentUris, JobModel jobDetails, bool generateDetailsWithAi, string userContextText, string userContextFileUrl, string promptKey = "renovation-00-initial-analysis.txt")
        {
            _logger.LogInformation("START: PerformRenovationAnalysisAsync for User {UserId}, Job {JobId}", userId, jobDetails.Id);

            try
            {
                _logger.LogInformation("Fetching renovation persona prompt.");
                var personaPrompt = await _promptManager.GetPromptAsync("RenovationPrompts/", "renovation-persona.txt");
                _logger.LogInformation("Fetching initial renovation analysis prompt.");
                var initialAnalysisPrompt = await _promptManager.GetPromptAsync("RenovationPrompts/", promptKey);

                _logger.LogInformation("Getting user context as string.");
                var userContext = await GetUserContextAsString(userContextText, userContextFileUrl);

                var initialUserPrompt = $"{initialAnalysisPrompt}\n\n{userContext}";

                _logger.LogInformation("Calling StartMultimodalConversationAsync for renovation analysis.");
                var (initialResponse, conversationId) = await _aiService.StartMultimodalConversationAsync(userId, documentUris, personaPrompt, initialUserPrompt);
                _logger.LogInformation("Started multimodal conversation {ConversationId} for user {UserId}. Initial response length: {Length}", conversationId, userId, initialResponse?.Length ?? 0);

                if (initialResponse.Contains("BLUEPRINT FAILURE", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Initial renovation analysis failed for conversation {ConversationId}. Triggering corrective action.", conversationId);
                    return await HandleFailureAsync(conversationId, userId, documentUris, initialResponse);
                }

                _logger.LogInformation("Blueprint fitness check PASSED for renovation conversation {ConversationId}. Proceeding with full sequential analysis.", conversationId);

                if (generateDetailsWithAi)
                {
                    _logger.LogInformation("Parsing and saving AI job details for Job {JobId}", jobDetails.Id);
                    await ParseAndSaveAiJobDetails(jobDetails.Id, initialResponse);
                }

                _logger.LogInformation("Executing sequential renovation prompts for conversation {ConversationId}", conversationId);
                return await ExecuteSequentialRenovationPromptsAsync(conversationId, userId, initialResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXCEPTION in PerformRenovationAnalysisAsync for User {UserId}, Job {JobId}", userId, jobDetails.Id);
                throw;
            }
            finally
            {
                _logger.LogInformation("END: PerformRenovationAnalysisAsync for User {UserId}, Job {JobId}", userId, jobDetails.Id);
            }
        }

        public async Task<AnalysisResponseDto> PerformComparisonAnalysisAsync(ComparisonAnalysisRequestDto request, List<IFormFile> pdfFiles)
        {
            string promptFileName = request.ComparisonType switch
            {
                ComparisonType.Vendor => "vendor-comparison-prompt.pdf",
                ComparisonType.Subcontractor => "subcontractor-comparison-prompt.pdf",
                _ => throw new System.ArgumentException("Invalid comparison type")
            };

            var prompt = await _promptManager.GetPromptAsync("ComparisonPrompts/", promptFileName);

            var combinedPdfText = new StringBuilder();
            foreach (var pdfFile in pdfFiles)
            {
                using var memoryStream = new MemoryStream();
                await pdfFile.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                var pdfText = await _pdfTextExtractionService.ExtractTextAsync(memoryStream);
                combinedPdfText.AppendLine(pdfText);
            }

            var fullPrompt = $"{prompt}\n\n{combinedPdfText}";

            var (analysisResult, conversationId) = await _aiService.StartMultimodalConversationAsync(request.UserId, null, fullPrompt, "Analyze the document based on the provided details.");

            return new AnalysisResponseDto
            {
                AnalysisResult = analysisResult,
                ConversationId = conversationId
            };
        }

        public async Task<string> GenerateRebuttalAsync(string conversationId, string clientQuery)
        {
            var rebuttalPrompt = await _promptManager.GetPromptAsync("", "prompt-22-rebuttal.txt") + $"\n\n**Client Query to Address:**\n{clientQuery}";
            var (response, _) = await _aiService.ContinueConversationAsync(conversationId, "system-user", rebuttalPrompt, null, false);
            return response;
        }

        public async Task<string> GenerateRevisionAsync(string conversationId, string revisionRequest)
        {
            var revisionPrompt = await _promptManager.GetPromptAsync("", "prompt-revision.txt") + $"\n\n**Revision Request:**\n{revisionRequest}";
            var (response, _) = await _aiService.ContinueConversationAsync(conversationId, "system-user", revisionPrompt, null, false);
            return response;
        }

        private async Task<string> ExecuteSequentialPromptsAsync(string conversationId, string userId, string initialResponse)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append(initialResponse);

            var promptNames = new[] {
                "prompt-01-sitelogistics", "prompt-02-groundwork", "prompt-03-framing",
                "prompt-04-roofing", "prompt-05-exterior", "prompt-06-electrical",
                "prompt-07-plumbing", "prompt-08-hvac", "prompt-09-insulation",
                "prompt-10-drywall", "prompt-11-painting", "prompt-12-trim",
                "prompt-13-kitchenbath", "prompt-14-flooring", "prompt-15-exteriorflatwork",
                "prompt-16-cleaning", "prompt-17-costbreakdowns", "prompt-18-riskanalyst",
                "prompt-19-timeline", "prompt-20-environmental", "prompt-21-closeout"
            };

            string lastResponse;
            int step = 1;
            foreach (var promptName in promptNames)
            {
                _logger.LogInformation("Executing step {Step} of {TotalSteps}: {PromptName} for conversation {ConversationId}", step, promptNames.Length, promptName, conversationId);
                var promptText = await _promptManager.GetPromptAsync("", $"{promptName}.txt");
                (lastResponse, _) = await _aiService.ContinueConversationAsync(conversationId, userId, promptText, null, true);

                await _conversationRepo.AddMessageAsync(new Message { ConversationId = conversationId, Role = "model", Content = lastResponse });

                stringBuilder.Append("\n\n---\n\n");
                stringBuilder.Append(lastResponse);

                step++;
            }

            _logger.LogInformation("Full sequential analysis completed successfully for conversation {ConversationId}", conversationId);

            return stringBuilder.ToString();
        }

        private async Task<string> HandleFailureAsync(string conversationId, string userId, IEnumerable<string> documentUrls, string failedResponse)
        {
            _logger.LogInformation("Failure prompt called for conversation {ConversationId}", conversationId);
            var correctivePrompt = await _promptManager.GetPromptAsync(null, FailureCorrectiveActionKey);
            var correctiveInput = $"{correctivePrompt}\n\nOriginal Failed Response:\n{failedResponse}";

            _logger.LogInformation("Calling ContinueConversationAsync for corrective action.");
            var (response, _) = await _aiService.ContinueConversationAsync(conversationId, userId, correctiveInput, null, true);

            return response;
        }

        private async Task<string> GetUserContextAsString(string userContextText, string userContextFileUrl)
        {
           var contextBuilder = new StringBuilder();
           if (!string.IsNullOrWhiteSpace(userContextText) && !userContextText.Contains("Analysis started with selected prompts:"))
           {
               contextBuilder.AppendLine("## User-Provided Context");
               contextBuilder.AppendLine(userContextText);
           }

           if (!string.IsNullOrWhiteSpace(userContextFileUrl))
           {
               try
               {
                   var (contentStream, _, _) = await _azureBlobService.GetBlobContentAsync(userContextFileUrl);
                   using var reader = new StreamReader(contentStream);
                   var fileContent = await reader.ReadToEndAsync();

                   if (!string.IsNullOrWhiteSpace(fileContent))
                   {
                       if (contextBuilder.Length == 0)
                       {
                           contextBuilder.AppendLine("## User-Provided Context");
                       }
                       contextBuilder.AppendLine("\n--- Context File Content ---");
                       contextBuilder.AppendLine(fileContent);
                   }
               }
               catch (Exception ex)
               {
                   _logger.LogError(ex, "Failed to read user context file from URL: {Url}", userContextFileUrl);
               }
           }

           return contextBuilder.ToString();
        }

       private async Task ParseAndSaveAiJobDetails(int jobId, string aiResponse)
       {
           try
           {
               var jsonRegex = new Regex(@"```json\s*(\{.*?\})\s*```", RegexOptions.Singleline);
               var match = jsonRegex.Match(aiResponse);
               if (match.Success)
               {
                   var json = match.Groups[1].Value;
                   var extractedData = JsonSerializer.Deserialize<JobModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                   var jobToUpdate = await _context.Jobs.FindAsync(jobId);
                   if (jobToUpdate != null && extractedData != null)
                   {
                       jobToUpdate.ProjectName = extractedData.ProjectName ?? jobToUpdate.ProjectName;
                       jobToUpdate.WallStructure = extractedData.WallStructure ?? jobToUpdate.WallStructure;
                       jobToUpdate.RoofStructure = extractedData.RoofStructure ?? jobToUpdate.RoofStructure;
                       jobToUpdate.Foundation = extractedData.Foundation ?? jobToUpdate.Foundation;
                       jobToUpdate.Finishes = extractedData.Finishes ?? jobToUpdate.Finishes;
                       jobToUpdate.ElectricalSupplyNeeds = extractedData.ElectricalSupplyNeeds ?? jobToUpdate.ElectricalSupplyNeeds;
                       jobToUpdate.Stories = extractedData.Stories > 0 ? extractedData.Stories : jobToUpdate.Stories;
                       jobToUpdate.BuildingSize = extractedData.BuildingSize > 0 ? extractedData.BuildingSize : jobToUpdate.BuildingSize;
                       await _context.SaveChangesAsync();
                   }
               }
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Failed to extract and update job details from AI response.");
           }
       }

       private async Task<string> ExecuteSequentialRenovationPromptsAsync(string conversationId, string userId, string initialResponse)
       {
           var stringBuilder = new StringBuilder();
           stringBuilder.Append(initialResponse);

           var promptNames = new[] {
               "renovation-01-demolition", "renovation-02-structural-alterations", "renovation-03-rough-in-mep",
               "renovation-04-insulation-drywall", "renovation-05-interior-finishes", "renovation-06-fixtures-fittings-equipment",
               "renovation-07-cost-breakdown-summary", "renovation-08-project-timeline", "renovation-09-environmental-impact",
               "renovation-10-final-review-rebuttal"
           };

           string lastResponse;
           int step = 1;
           foreach (var promptName in promptNames)
           {
               _logger.LogInformation("Executing step {Step} of {TotalSteps}: {PromptName} for conversation {ConversationId}", step, promptNames.Length, promptName, conversationId);
               var promptText = await _promptManager.GetPromptAsync("RenovationPrompts/", $"{promptName}.txt");
               (lastResponse, _) = await _aiService.ContinueConversationAsync(conversationId, userId, promptText, null, true);

               await _conversationRepo.AddMessageAsync(new Message { ConversationId = conversationId, Role = "model", Content = lastResponse });

               stringBuilder.Append("\n\n---\n\n");
               stringBuilder.Append(lastResponse);

               step++;
           }

           _logger.LogInformation("Full sequential renovation analysis completed successfully for conversation {ConversationId}", conversationId);

           return stringBuilder.ToString();
       }

        public async Task<string> AnalyzeBidsAsync(List<BidModel> bids, string comparisonType)
        {
            string promptKey = comparisonType.Equals("Vendor", StringComparison.OrdinalIgnoreCase)
                ? "vendor-comparison-prompt.txt"
                : "subcontractor-comparison-prompt.txt";

            var prompt = await _promptManager.GetPromptAsync("ComparisonPrompts/", promptKey);

            var bidsDetails = new List<object>();
            foreach (var bid in bids)
            {
                string quoteText = bid.Task ?? "No detailed quote text provided.";
                if (bid.Quote != null && bid.Quote.Length > 0)
                {
                    try
                    {
                        using var memoryStream = new MemoryStream(bid.Quote);
                        quoteText = await _pdfTextExtractionService.ExtractTextAsync(memoryStream);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to extract text from PDF for bid {BidId}", bid.Id);
                        quoteText = "Error extracting text from PDF.";
                    }
                }

                bidsDetails.Add(new
                {
                    bid.Id,
                    bid.Amount,
                    bid.User?.ProbuildRating,
                    bid.User?.GoogleRating,
                    bid.User?.JobPreferences,
                    QuoteDetails = quoteText
                });
            }

            var bidsJson = JsonSerializer.Serialize(bidsDetails);
            var fullPrompt = $"{prompt}\n\nBids:\n{bidsJson}";

            var (analysisResult, _) = await _aiService.StartMultimodalConversationAsync("system-user", null, fullPrompt, $"Analyze the provided {comparisonType} bids and return the top 3 candidates.");

            return analysisResult;
        }

        public async Task<string> GenerateFeedbackForUnsuccessfulBidderAsync(BidModel bid, BidModel winningBid)
        {
            var user = bid.User;
            if (user != null)
            {
                bool isFreeTier = user.SubscriptionPackage == "Basic (Free) ($0.00)" ||
                                  string.IsNullOrEmpty(user.SubscriptionPackage) ||
                                  user.SubscriptionPackage.ToUpper() == "BASIC";

                if (isFreeTier)
                {
                    return "Feedback reports are only available for users on a paid subscription tier.";
                }
            }

            var prompt = await _promptManager.GetPromptAsync("ComparisonPrompts/", "unsuccessful-bid-prompt.txt");

            var unsuccessfulQuote = await _context.Quotes.FindAsync(bid.Id.ToString());
            var winningQuote = await _context.Quotes.FindAsync(winningBid.Id.ToString());

            var unsuccessfulBidAnalysis = new
            {
                bid.Id,
                bid.Amount,
                QuoteDetails = unsuccessfulQuote
            };

            var winningBidBenchmark = new
            {
                winningBid.Amount,
                QuoteDetails = winningQuote
            };

            var promptInput = new
            {
                ProjectName = bid.Job?.ProjectName,
                WorkPackage = bid.Job?.JobType,
                OurCompanyName = "Probuild", // Or fetch dynamically
                UnsuccessfulSubcontractorName = user?.UserName,
                AnalysisOfUnsuccessfulQuotation = JsonSerializer.Serialize(unsuccessfulBidAnalysis),
                WinningBidBenchmark = JsonSerializer.Serialize(winningBidBenchmark)
            };

            var fullPrompt = prompt
                .Replace("[e.g., The Falcon Heights Residential Development]", promptInput.ProjectName)
                .Replace("[e.g., Structural Steel Fabrication and Erection]", promptInput.WorkPackage)
                .Replace("[Your Company Name]", promptInput.OurCompanyName)
                .Replace("[Enter the name of the company you are writing to]", promptInput.UnsuccessfulSubcontractorName)
                .Replace("[Paste the detailed analysis of the subcontractor's quote here, including price, schedule, inclusions, exclusions, compliance notes.]", promptInput.AnalysisOfUnsuccessfulQuotation)
                .Replace("[Summarize the key advantages of the winning bid. For example: \"Final price was R 1,150,000 (8% lower). Proposed schedule was 16 weeks (2 weeks shorter). Fully compliant with specifications. Included a detailed plan for managing material price volatility.\"]", promptInput.WinningBidBenchmark);


            var (analysisResult, _) = await _aiService.StartMultimodalConversationAsync("system-user", null, fullPrompt, "Generate a feedback report for the provided bid.");

            return analysisResult;
        }
   }
}

