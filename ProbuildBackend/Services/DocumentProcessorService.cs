using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OpenAI; // Main OpenAI SDK namespace
using OpenAI.Chat; // For Chat-specific types
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;

namespace ProbuildBackend.Services
{
    public class DocumentProcessorService
    {
        private readonly AzureBlobService _azureBlobService;
        private readonly ChatClient _chatClient;
        private readonly Dictionary<string, decimal> _materialCosts;

        public DocumentProcessorService(AzureBlobService azureBlobService, IConfiguration configuration)
        {
            _azureBlobService = azureBlobService ?? throw new ArgumentNullException(nameof(azureBlobService));
            string apiKey = "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" ?? throw new ArgumentNullException("OpenAI API key is missing");
            var openAIClient = new OpenAIClient(apiKey);
            _chatClient = openAIClient.GetChatClient("gpt-3.5-turbo"); // Specify the model here

            // Sample cost database (replace with a real source later)
            _materialCosts = new Dictionary<string, decimal>
            {
                { "concrete", 100m }, // $ per cubic yard
                { "steel rebar", 0.8m }, // $ per pound
                { "lumber", 2.5m } // $ per board foot
            };
        }

        public async Task<BomWithCosts> ProcessDocumentAsync(string blobUrl)
        {
            // Step 1: Retrieve and extract text from the document
             string documentText = await ExtractTextFromBlob(blobUrl);

            // Step 2: Generate BOM with OpenAI
            var bom = await GenerateBomFromText(documentText);

            // Step 3: Calculate costs
            return CalculateCosts(bom);
        }

        private async Task<string> ExtractTextFromBlob(string blobUrl)
        {
            var (contentStream, _, _) = await _azureBlobService.GetBlobContentAsync(blobUrl);
            using (var reader = new PdfReader(contentStream))
            using (var pdfDoc = new PdfDocument(reader))
            {
                var text = new StringBuilder();
                for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                {
                    text.Append(PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i)));
                }
                return text.ToString();
            }
        }

        private async Task<BillOfMaterials> GenerateBomFromText(string documentText)
        {
            try
            {

   
            string prompt = $@"
            You are an expert in construction document analysis. Extract a bill of materials (BOM) from the text below, including item names, quantities, and units. Return the result as a JSON object in this format:
            {{
                ""bill_of_materials"": [
                    {{""item"": ""item_name"", ""quantity"": number, ""unit"": ""unit_type""}}
                ]
            }}
            Document text:
            {documentText}
            ";

            // Create the chat messages
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a construction document parser."),
                new UserChatMessage(prompt)
            };

            // Configure chat completion options
            var chatOptions = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 50000 // Use MaxOutputTokens instead of MaxTokens
            };

            // Call the OpenAI Chat API
            ChatCompletion response = await _chatClient.CompleteChatAsync(messages, chatOptions);

            // Extract the JSON content from the response
            string jsonResponse = response.Content[0].Text;
            return JsonSerializer.Deserialize<BillOfMaterials>(jsonResponse);
            }
            catch (Exception ex)
            {

                throw ex;
            }
        }

        private BomWithCosts CalculateCosts(BillOfMaterials bom)
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

    // Data models (unchanged)
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
}