using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using System.Drawing;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using ProbuildBackend.Models;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Middleware;
using ProbuildBackend.Interface;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using Image = SixLabors.ImageSharp.Image;
using Point = SixLabors.ImageSharp.Point;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using Size = SixLabors.ImageSharp.Size;
using Microsoft.AspNetCore.Identity.UI.Services;


namespace ProbuildBackend.Services
{
    public class DocumentProcessorService
    {
        private readonly AzureBlobService _azureBlobService;
        private readonly ChatClient _chatClient;
        private readonly ChatClient _chatClient3Turbo;
        private readonly Dictionary<string, decimal> _materialCosts;
        private readonly OcrSettings _settings;
        private readonly ApplicationDbContext _context; // Add database context
        private readonly IHubContext<ProgressHub> _hubContext; // Add SignalR hub context
        private readonly IEmailSender _emailService; // Add this
        private List<string> FullResponseList = new List<string>();
        public List<string> AIText = new List<string>();
        public DocumentProcessorService(
            AzureBlobService azureBlobService,
            IConfiguration configuration,
            ApplicationDbContext context,
            IHubContext<ProgressHub> hubContext,
        IEmailSender emailService)
        {
            _azureBlobService = azureBlobService ?? throw new ArgumentNullException(nameof(azureBlobService));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));

            string apiKey = Environment.GetEnvironmentVariable("GPTAPIKEY")
                      ?? configuration["ChatGPTAPI:APIKey"]; 
               
            var openAIClient = new OpenAIClient(apiKey);
            var openAIClient3 = new OpenAIClient(apiKey);
            _chatClient = openAIClient.GetChatClient("gpt-3.5-turbo");
            _chatClient3Turbo = openAIClient3.GetChatClient("gpt-4o-mini");

            _materialCosts = new Dictionary<string, decimal>
            {
                { "concrete", 100m },
                { "steel rebar", 0.8m },
                { "lumber", 2.5m }
            };

            _settings = new OcrSettings
            {
                Dpi = 150,
                MaxImageWidth = 2048,
                MaxImageHeight = 2048,
                MaxConcurrentPages = 2,
                ThrottleDelayMs = 3500,
                MaxTokens = 4000
            };
        }

        public async Task<(BomWithCosts BomWithCosts, MaterialsEstimate MaterialsEstimate, List<string> FullResponse)> ProcessDocumentAsync(string blobUrl)
        {
            List<string> documentText = await ExtractTextFromBlob(blobUrl); // This is the full response
           // var bom = await GenerateBomFromText(documentText);
            //var bomWithCosts = CalculateCosts(bom);
            var materialsEstimate = await ExtractMaterialsEstimateFromText(documentText);
            return (null, materialsEstimate, documentText); // Return the full response
        }

        // New method to process multiple documents for a job
        public async Task ProcessDocumentsForJobAsync(int jobId, List<string> documentUrls, string connectionId)
        {
            try
            {
                var bomResults = new List<BomWithCosts>();
                var materialsEstimates = new List<MaterialsEstimate>();

                // Retrieve the job to get the UserId
                var job = await _context.Jobs.FindAsync(jobId);
                if (job == null)
                {
                    throw new InvalidOperationException($"Job with ID {jobId} not found.");
                }

                // Retrieve the user's email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == job.UserId);
                if (user == null)
                {
                    Console.WriteLine($"User with ID {job.UserId} not found. Cannot send email.");
                }
                bool AIProcessed = false;
                // Process each document and save its results
                foreach (var url in documentUrls)
                {
                    // Find the document ID by BlobUrl
                    var document = await _context.JobDocuments
                        .FirstOrDefaultAsync(doc => doc.BlobUrl == url && doc.JobId == jobId);

                    if (document == null)
                    {
                        Console.WriteLine($"Document with BlobUrl {url} not found for JobId {jobId}. Skipping...");
                        continue;
                    }

                    // Process the document
                    var (bomWithCosts, materialsEstimate, fullResponse) = await ProcessDocumentAsync(url);

                    FullResponseList = fullResponse;
                    AIProcessed = true;

                   // Environment.Exit(0);
                   // Add to the lists for consolidation
                    bomResults.Add(bomWithCosts);
                    materialsEstimates.Add(materialsEstimate);
                }

                // Send an email notification
                if (AIProcessed)
                {
                    string RefineText = string.Empty;

                    foreach (var item in FullResponseList)
                    {
                        RefineText += " " + item;
                    }

                    string refinedText = await RefineTextWithAiAsync(RefineText, "");

                    // Save the results to the DocumentProcessingResults table
                    var processingResult = new DocumentProcessingResult
                    {
                        JobId = jobId,
                        DocumentId = 0,
                        BomJson = JsonSerializer.Serialize(""),
                        MaterialsEstimateJson = JsonSerializer.Serialize(""),
                        FullResponse = refinedText, // Save the full response
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.DocumentProcessingResults.Add(processingResult);
                    await _context.SaveChangesAsync();

                    if (user != null)
                    {
                        var subject = $"AI Processing Complete for Job {jobId}";
                        var body = $@"<h2>AI Processing Complete</h2>
                              <p>The AI has finished processing a document for your job.</p>
                              <p><strong>Job ID:</strong> {jobId}</p>
                              <p><strong>Full Response Preview:</strong></p>
                              <p>Check the application for full details.</p>";

                        try
                        {
                            await _emailService.SendEmailAsync(user.Email, subject, body);
                        }
                        catch (Exception ex)
                        {
                            //Console.WriteLine($"Failed to send email for DocumentId {document.Id}: {ex.Message}");
                            // Log the error, but don't fail the entire job
                        }
                    }
                }
                // Consolidate the BOM results
                // var consolidatedBom = ConsolidateBomResults(bomResults);



                // Notify the user via SignalR
                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync("JobProcessingComplete", new
                    {
                        JobId = jobId,
                       // Bom = consolidatedBom,
                        Message = "Document processing complete. BOM generated."
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing documents for job {jobId}: {ex.Message}");

                // Update the job status to indicate failure
                var job = await _context.Jobs.FindAsync(jobId);
                if (job != null)
                {
                    job.Status = "FAILED";
                    await _context.SaveChangesAsync();
                }

                // Notify the user of the failure via SignalR
                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync("JobProcessingFailed", new
                    {
                        JobId = jobId,
                        Error = ex.Message
                    });
                }

                throw; // Re-throw for Hangfire retries
            }
        }

        // Helper method to consolidate BOM results from multiple documents
        private BomWithCosts ConsolidateBomResults(List<BomWithCosts> bomResults)
        {
            var consolidatedItems = new Dictionary<string, BomItemWithCost>();

            foreach (var bom in bomResults)
            {
                foreach (var item in bom.BillOfMaterials)
                {
                    string key = $"{item.Item}_{item.Unit}".ToLower();
                    if (consolidatedItems.ContainsKey(key))
                    {
                        // Merge quantities for the same item and unit
                        consolidatedItems[key].Quantity += item.Quantity;
                        consolidatedItems[key].TotalItemCost += item.TotalItemCost;
                    }
                    else
                    {
                        consolidatedItems[key] = new BomItemWithCost
                        {
                            Item = item.Item,
                            Quantity = item.Quantity,
                            Unit = item.Unit,
                            CostPerUnit = item.CostPerUnit,
                            TotalItemCost = item.TotalItemCost
                        };
                    }
                }
            }

            var consolidatedBom = new BomWithCosts
            {
                BillOfMaterials = consolidatedItems.Values.ToList(),
                TotalCost = consolidatedItems.Values.Sum(item => item.TotalItemCost)
            };

            return consolidatedBom;
        }

        public async Task<List<string>> ExtractTextFromBlob(string blobUrl)
        {
            try
            {
                var (contentStream, contentType, originalFileName) = await _azureBlobService.GetBlobContentAsync(blobUrl);

                if (contentType != "application/pdf")
                {
                    throw new InvalidOperationException($"Blob at {blobUrl} is not a PDF. Content-Type: {contentType}");
                }

                Console.WriteLine($"Extracting text from PDF at {blobUrl} using OpenAI ChatGPT API.");
                string extractedText = await ExtractTextWithOcr(blobUrl, contentStream);

                if (string.IsNullOrEmpty(extractedText))
                {
                    throw new InvalidOperationException($"No text could be extracted from PDF at {blobUrl}.");
                }
                AIText.Add(extractedText);
                Console.WriteLine($"Extracted {extractedText.Length} characters from PDF at {blobUrl}.");

                // Refine the extracted text using the OpenAI API
             
                Console.WriteLine($"Refined text to {AIText.Count} characters for PDF at {blobUrl}.");

                return AIText;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract text from blob: {blobUrl}", ex);
            }
        }

        private async Task<string> ExtractTextWithOcr(string blobUrl, Stream contentStream)
        {
            try
            {

                //Console.WriteLine($"LD_LIBRARY_PATH: {Environment.GetEnvironmentVariable("LD_LIBRARY_PATH")}");
                //Console.WriteLine($"libpdfium.so exists: {File.Exists("/app/runtimes/linux-x64/native/libpdfium.so")}");
                //Console.WriteLine($"pdfium.dll symlink exists: {File.Exists("/app/runtimes/linux-x64/native/pdfium.dll")}");

                // Step 1: Convert PDF pages to images
                var pageImages = await ConvertPdfToImagesAsync(blobUrl, contentStream);

                // Step 2: Process pages in parallel
                var results = await ProcessPagesInParallelAsync(pageImages, blobUrl);

                // Step 3: Combine results
                return string.Join("\n", results);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed OCR for blob: {blobUrl}", ex);
            }
        }

        private async Task<List<(int PageIndex, byte[] ImageBytes)>> ConvertPdfToImagesAsync(string blobUrl, Stream contentStream)
        {
            var pageImages = new List<(int, byte[])>();
            try
            {
                using var pdfReader = new PdfReader(contentStream);
                using var pdfDocument = new PdfDocument(pdfReader);

                for (int pageIndex = 0; pageIndex < pdfDocument.GetNumberOfPages(); pageIndex++)
                {
                    var page = pdfDocument.GetPage(pageIndex + 1); // iText7 pages are 1-based
                    var pageSize = page.GetPageSize();
                    int width = Math.Max((int)(pageSize.GetWidth() * _settings.Dpi / 72), 1);
                    int height = Math.Max((int)(pageSize.GetHeight() * _settings.Dpi / 72), 1);

                    // Create a blank image with ImageSharp
                    using var image = new Image<Rgba32>(width, height);

                    // Fill the image with a white background
                    image.Mutate(ctx => ctx.BackgroundColor(SixLabors.ImageSharp.Color.White));

                    // Render the PDF page to an image using PdfCanvasProcessor
                    var strategy = new ImageRenderListener(image);
                    var processor = new PdfCanvasProcessor(strategy);
                    processor.ProcessPageContent(page);

                    // Resize the image using ImageSharp
                    int maxWidth = _settings.MaxImageWidth;
                    int maxHeight = _settings.MaxImageHeight;
                    image.Mutate(x =>
                    {
                        x.Resize(new ResizeOptions
                        {
                            Size = new Size(maxWidth, maxHeight),
                            Mode = ResizeMode.Max
                        });
                    });

                    // Save the image to a memory stream as PNG
                    using var memoryStream = new MemoryStream();
                    await image.SaveAsync(memoryStream, new PngEncoder());
                    pageImages.Add((pageIndex, memoryStream.ToArray()));
                }

                if (!pageImages.Any())
                    throw new InvalidOperationException($"No images were generated from PDF at {blobUrl}.");

                return pageImages;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to convert PDF to images: {blobUrl}", ex);
            }
        }

        private async Task<List<string>> ProcessPagesInParallelAsync(List<(int PageIndex, byte[] ImageBytes)> pageImages, string blobUrl)
        {
            // Process in batches of 2 pages
            const int batchSize = 2;
            var results = new ConcurrentBag<(int PageIndex, string Text)>();

            for (int i = 0; i < pageImages.Count; i += batchSize)
            {
                var batch = pageImages.Skip(i).Take(batchSize).ToList();
                var batchTasks = batch.Select(async page =>
                {
                    try
                    {
                        var text = await AnalyzePageWithAiAsync(page.ImageBytes, page.PageIndex, blobUrl);
                        results.Add((page.PageIndex, text));
                        Console.WriteLine($"✅ Page {page.PageIndex + 1}: Success - {text.Length} chars.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Page {page.PageIndex + 1} failed: {ex.Message}");
                        results.Add((page.PageIndex, $"[Page {page.PageIndex + 1}: OCR failed - {ex.Message}]"));
                    }
                });

                await Task.WhenAll(batchTasks);

                // Wait between batches to avoid rate limits
                if (i + batchSize < pageImages.Count)
                {
                    await Task.Delay(5000);
                }
            }

            return results.OrderBy(r => r.PageIndex).Select(r => r.Text).ToList();
        }

        private async Task<string> AnalyzePageWithAiAsync(byte[] imageBytes, int pageIndex, string blobUrl)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(@"
                        You are a senior quantity surveyor and construction analyst reviewing architectural drawings and construction plans from a multi-page PDF.

                        Your task is to extract and interpret key information from each page and return a detailed, structured markdown report for use in construction planning software.

                        Focus on:
                        1. Identifying the building type (e.g., residential, commercial) and design characteristics (e.g., single-family home, duplex, high-rise).
                        2. Describing the layout, rooms, access points, vertical circulation, and any unique architectural features.
                        3. Extracting a Bill of Materials with visible or inferable quantities. Format this in a markdown table like:
                        | Item | Quantity | Unit | Location/Notes |
                        4. Estimating a cost range using standard construction cost rates (e.g., $/sqft) and basic assumptions. Mention if area needs to be estimated.
                        5. Documenting all dimensions, symbols, and legends found on the page and their function.

                        Respond using this markdown structure:
                        - **Building Description**
                        - **Layout & Design**
                        - **Materials List with rough estimate of quantities if you cannot figure out the quantity, just return somthing that seems about right**
                        - **Cost Estimate**
                        - **Other Notes (legends, symbols, dimensions, etc.)**

                        Be professional, clear, and infer missing values based on typical construction conventions when needed."),

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
                await Task.Delay(_settings.ThrottleDelayMs);

                return response.Content?.Count > 0 && !string.IsNullOrEmpty(response.Content[0].Text)
                    ? response.Content[0].Text
                    : $"[Page {pageIndex + 1}: No text extracted]";
            });
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

        private async Task<string> RefineTextWithAiAsync(string extractedText, string blobUrl)
        {
            try
            {
                var messages = new List<ChatMessage>
{
    new SystemChatMessage(@"
        You are a senior construction documentation expert tasked with refining raw construction analysis from a multi-page plan document into a cohesive, professional report. Your output must be highly detailed, technically precise, and comprehensive, matching the depth of a human expert’s analysis.

        Your goals are:
        1. **Merge Redundant Data**: Consolidate repeated material entries or sections across multiple pages into a single, unified set. For example, combine all 'framing lumber' mentions into one total with a clear justification.
        2. **Resolve Inconsistencies**: Standardize units (e.g., board feet for lumber, square feet for drywall), terminology (e.g., 'roofing shingles' vs. 'roofing material'), and formatting. Correct any contradictory data (e.g., varying square footage) with logical assumptions.
        3. **Improve Structure**: Organize the report into clear, logical sections: 'Building Description,' 'Layout & Design,' and 'Construction Timeline by Task Category.' Use markdown headers (##) and subheaders (###) for readability. The 'Construction Timeline by Task Category' section must dynamically determine numbered categories based on the Bill of Materials, with each BOM item mapped as a main task with its appropriate MasterFormat division or section code (e.g., '# 1. Foundation Concrete (03 30 00)'). Under each main task, list inferred subtasks with specific MasterFormat section codes (e.g., 'Pouring concrete (03 31 00)') without bullets or hyphens, using bolded titles (e.g., **Pouring concrete**). Include **Duration**, **Start Date**, and **End Date** for each main task, and for each subtask, include **Duration**, **Start Date**, and **End Date** within a dedicated block under the bolded subtask title, separated by a horizontal rule (`---`) after each subtask to clearly mark the end. Include notes where applicable (e.g., 'Final fixture install happens after finishes').
        4. **Enhance Clarity**: Present all data in well-labeled markdown with detailed descriptions, bullet points for layout features, and tables for materials where needed. Ensure the report is actionable for construction teams and planners, with subtasks visually distinct using bolded titles and separators, avoiding bullets or hyphens.
        5. **Generate Final Bill of Materials**: At the end, provide a single, consolidated materials estimate categorized under the 'Construction Timeline by Task Category' sections using MasterFormat codes. List materials and quantities with justifications in notes.
        6. **Provide MasterFormat Codes**: Include accurate MasterFormat division or section codes and titles for each main task (mapped from the BOM) and inferred subtasks, referencing the authoritative source at https://crmservice.csinet.org/widgets/masterformat/numbersandtitles.aspx. If a specific task or subtask lacks a direct code, use the closest appropriate section and note the assumption.
        7. **Categorize by Tasks**: Map each item in the Bill of Materials as a main task under a dynamically generated category number, and infer relevant subtasks based on the material and its typical construction processes.
        8. **Group Tasks Together**: Ensure all subtasks within each main task are listed together with estimated durations and dates sequenced logically from April 16, 2025.

        Additional Instructions:
        - **Depth**: Provide exhaustive details, such as specific room counts (e.g., 3–4 bedrooms, 2–3 bathrooms), unique architectural features (e.g., gabled roofs, open floor plans), and material specifics (e.g., double-pane windows, asphalt shingles).
        - **Assumptions**: If data is incomplete or lacks a breakdown, make reasonable assumptions (e.g., assume 2,750 sqft if size varies, infer subtasks like 'excavation' or 'drywall installation' based on materials) and explain them in the report. If a task or subtask lacks a specific MasterFormat code, assume the nearest relevant section and document the assumption.
        - **Methodology**: Briefly outline how you consolidated data (e.g., averaging quantities, prioritizing higher estimates for structural items) and inferred main tasks and subtasks from the Bill of Materials (e.g., using industry standards) in a subsection under the Construction Timeline.
        - **Technical Precision**: Use industry-standard units and terms, avoiding vague descriptions. For example, specify 'cubic yards' for concrete, not 'amount.'
        - **Timeline**: Base durations on typical construction schedules for a 2,500–3,000 sqft single-family home, adjusted based on the Bill of Materials quantities, with **Start Date** and **End Date** sequenced logically from April 16, 2025. Subtask durations should sum to the main task duration where applicable.
        - **Dynamic Task and Subtask Inference**: Determine the number and titles of main tasks directly from the Bill of Materials items (e.g., 'Foundation Concrete' as a task). Infer related subtasks (e.g., 'Pouring concrete', 'Formwork') based on the material and its construction process. Assign appropriate MasterFormat codes to both main tasks and subtasks.
        - **MasterFormat Reference**: Use the MasterFormat codes and titles from https://crmservice.csinet.org/widgets/masterformat/numbersandtitles.aspx to categorize tasks and materials accurately, placing codes in parentheses next to main task titles and subtask descriptions.
        - **Subtask Formatting**: Format each subtask with a bolded title (e.g., **Pouring concrete**) without bullets or hyphens, followed by a dedicated block with indented details (Duration, Start Date, End Date), and end each subtask block with a horizontal rule (`---`) to clearly separate subtasks.

        Return the full, final report in markdown format only. Do not include commentary, metadata, or explanations outside the report itself. Ensure the output is as detailed as a human expert’s analysis, with no omissions of key construction elements.
    "),

    new UserChatMessage("Here is the extract:\n```\n" + extractedText + "\n```")
};

                var chatOptions = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = _settings.MaxTokens,
                    Temperature = (float?)0.2,
                    TopP = (float?)0.9
                };
               ChatCompletion response = await _chatClient3Turbo.CompleteChatAsync(messages, chatOptions);

                return response.Content?.Count > 0 && !string.IsNullOrEmpty(response.Content[0].Text)
                    ? response.Content[0].Text
                    : extractedText;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Refinement failed for {blobUrl}: {ex.Message}");
                return extractedText;
            }
        }

        private async Task<MaterialsEstimate> ExtractMaterialsEstimateFromText(List<string> refinedText)
        {
            try
            {
                //var lines = refinedText.Split('\n');
                var estimateLines = new List<string>();
                bool inEstimateSection = false;

                foreach (var line in refinedText)
                {

                    // Look for the materials list section (match variations of the header)
                    if (line.Trim().StartsWith("## Materials List") || line.Trim().StartsWith("# Materials List") ||
                        line.Trim().StartsWith("## Materials Estimate") || line.Trim().StartsWith("# Materials Estimate") || line.Trim().StartsWith("## **Materials List**"))
                    {
                        inEstimateSection = true;
                        continue;
                    }
                    if (inEstimateSection && line.Trim().StartsWith("#"))
                    {
                        inEstimateSection = false;
                        break;
                    }
                    if (inEstimateSection && line.Contains("|") && !line.Contains("---"))
                    {
                        estimateLines.Add(line);
                    }
                }

                var materials = new List<MaterialEstimateItem>();
                foreach (var line in estimateLines)
                {
                    var parts = line.Split('|').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToArray();
                    if (parts.Length >= 3 && decimal.TryParse(parts[1], out decimal quantity))
                    {
                        materials.Add(new MaterialEstimateItem
                        {
                            Item = parts[0],
                            TotalQuantity = quantity,
                            Unit = parts[2]
                        });
                    }
                }

                return new MaterialsEstimate { Materials = materials };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to extract materials estimate: {ex.Message}");
                return new MaterialsEstimate { Materials = new List<MaterialEstimateItem>() };
            }
        }
        private Image<Rgba32> ResizeImage(Image image, int maxWidth, int maxHeight)
        {
            // Calculate the scaling ratio to fit within maxWidth and maxHeight
            var ratioX = (double)maxWidth / image.Width;
            var ratioY = (double)maxHeight / image.Height;
            var ratio = Math.Min(ratioX, ratioY);

            var newWidth = (int)(image.Width * ratio);
            var newHeight = (int)(image.Height * ratio);

            // Ensure the new dimensions are at least 1 to avoid invalid image size
            newWidth = Math.Max(1, newWidth);
            newHeight = Math.Max(1, newHeight);

            // Resize the image using ImageSharp
            image.Mutate(x =>
            {
                x.Resize(new ResizeOptions
                {
                    Size = new Size(newWidth, newHeight),
                    Mode = ResizeMode.Stretch, // Maintain aspect ratio within bounds
                    Sampler = KnownResamplers.NearestNeighbor // Equivalent to low interpolation
                });
            });

            // Set DPI (metadata, as ImageSharp doesn't have direct SetResolution like System.Drawing)
            if (image.Metadata.HorizontalResolution != _settings.Dpi || image.Metadata.VerticalResolution != _settings.Dpi)
            {
                image.Metadata.HorizontalResolution = _settings.Dpi;
                image.Metadata.VerticalResolution = _settings.Dpi;
            }

            return (Image<Rgba32>)image;
        }

        public async Task<BillOfMaterials> GenerateBomFromText(string documentText)
        {
            try
            {
                string prompt = @"
                You are an expert in construction document analysis, specializing in building plans. Extract a bill of materials (BOM) from the text below, including item names, quantities, and units. The text is from a building plan and may include tables, annotations, diagrams with labels, and other structured data. Look for lists, tables, or sections that specify materials, such as 'Item: Concrete, Quantity: 50, Unit: cubic yards'. If a table is present, it may be formatted with tab-separated columns (e.g., 'Item\tQuantity\tUnit'). If no clear BOM data is found, return an empty list. Return the result as a JSON object in this format:
                {
                    ""bill_of_materials"": [
                        {""item"": ""item_name"", ""quantity"": number, ""unit"": ""unit_type""}
                    ]
                }
                Document text:
                " + documentText;

                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage("You are a construction document parser specializing in building plans."),
                    new UserChatMessage(prompt)
                };

                var chatOptions = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 1000
                };

                ChatCompletion response = await _chatClient.CompleteChatAsync(messages, chatOptions);
                string jsonResponse = response.Content[0].Text;

                var bom = JsonSerializer.Deserialize<BillOfMaterials>(jsonResponse);
                if (bom == null || bom.BillOfMaterialsItems == null)
                {
                    return new BillOfMaterials { BillOfMaterialsItems = new List<BomItem>() };
                }

                return bom;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to generate BOM from document text", ex);
            }
        }

        public BomWithCosts CalculateCosts(BillOfMaterials bom)
        {
            decimal totalCost = 0;
            var bomWithCosts = new List<BomItemWithCost>();

            foreach (var item in bom.BillOfMaterialsItems)
            {
                string material = item.Item.ToLower();
                decimal costPerUnit = _materialCosts.ContainsKey(material) ? _materialCosts[material] : 0;
                decimal itemCost = item.Quantity * costPerUnit;
                totalCost += itemCost;

                bomWithCosts.Add(new BomItemWithCost
                {
                    Item = item.Item,
                    Quantity = item.Quantity,
                    Unit = item.Unit,
                    CostPerUnit = costPerUnit,
                    TotalItemCost = itemCost
                });
            }

            return new BomWithCosts { BillOfMaterials = bomWithCosts, TotalCost = totalCost };
        }
    }

    public class OcrSettings
    {
        public int Dpi { get; set; } = 72;
        public int MaxImageWidth { get; set; } = 1024;
        public int MaxImageHeight { get; set; } = 1024;
        public int MaxConcurrentPages { get; set; } = 4;
        public int ThrottleDelayMs { get; set; } = 2000;
        public int MaxTokens { get; set; } = 4000;
    }

    public class BillOfMaterials
    {
        public List<BomItem> BillOfMaterialsItems { get; set; } = new();
    }

    public class BomItem
    {
        public string Item { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
    }

    public class BomWithCosts
    {
        public List<BomItemWithCost> BillOfMaterials { get; set; } = new();
        public decimal TotalCost { get; set; }
    }

    public class BomItemWithCost
    {
        public string Item { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal CostPerUnit { get; set; }
        public decimal TotalItemCost { get; set; }
    }

    public class MaterialsEstimate
    {
        public List<MaterialEstimateItem> Materials { get; set; } = new();
    }

    public class MaterialEstimateItem
    {
        public string Item { get; set; }
        public decimal TotalQuantity { get; set; }
        public string Unit { get; set; }
    }

    // Helper class to render PDF content to ImageSharp image
    public class ImageRenderListener : IEventListener
    {
        private readonly Image<Rgba32> _image;

        public ImageRenderListener(Image<Rgba32> image)
        {
            _image = image;
        }

        public void EventOccurred(IEventData data, EventType type)
        {
            if (data is ImageRenderInfo imageData)
            {
                var pdfImage = imageData.GetImage();
                var imageBytes = pdfImage.GetImageBytes();
                using var img = Image.Load<Rgba32>(imageBytes);

                // Get image position and transformation
                var matrix = imageData.GetImageCtm();
                float x = matrix.Get(4); // Translation X
                float y = matrix.Get(5); // Translation Y

                // Adjust Y-coordinate for ImageSharp (PDF origin is bottom-left, ImageSharp is top-left)
                _image.Mutate(ctx => ctx.DrawImage(img, new Point((int)x, _image.Height - (int)y - img.Height), 1.0f));
            }
        }

        public ICollection<EventType> GetSupportedEvents()
        {
            return new List<EventType> { EventType.RENDER_IMAGE };
        }
    }
}