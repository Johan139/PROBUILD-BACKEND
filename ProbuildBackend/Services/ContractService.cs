using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;

namespace ProbuildBackend.Services
{
    public class ContractService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAiService _aiService;
        private readonly AzureBlobService _azureBlobService;
        private readonly IPromptManagerService _promptManager;

        public ContractService(
            ApplicationDbContext context,
            IAiService aiService,
            AzureBlobService azureBlobService,
            IPromptManagerService promptManager
        )
        {
            _context = context;
            _aiService = aiService;
            _azureBlobService = azureBlobService;
            _promptManager = promptManager;
        }

        public async Task<Contract> GenerateGeneralContractAsync(int jobId, string gcId)
        {
            var job = await _context
                .Jobs.Include(j => j.JobAddress)
                .FirstOrDefaultAsync(j => j.Id == jobId);

            var gc = await _context.Users.FindAsync(gcId);
            var gcAddress = await _context.UserAddress.FirstOrDefaultAsync(ua => ua.UserId == gcId);
            var client = await _context.ClientDetails.FirstOrDefaultAsync(cd => cd.JobId == jobId);

            if (job == null || gc == null)
            {
                throw new Exception(
                    "Could not find all required information to generate the contract."
                );
            }

            var clientName =
                client != null ? $"{client.FirstName} {client.LastName}".Trim() : "Client";
            var clientEmail = client?.Email ?? "Not Specified";

            var prompt = await _promptManager.GetPromptAsync(
                null,
                "general-contractor-client-contract-prompt.txt"
            );

            var latestProcessingResult = await _context
                .DocumentProcessingResults.AsNoTracking()
                .Where(r => r.JobId == jobId && !string.IsNullOrWhiteSpace(r.FullResponse))
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            var executiveSummary = ExtractExecutiveSummary(latestProcessingResult?.FullResponse);
            var executiveSummaryContext = string.IsNullOrWhiteSpace(executiveSummary)
                ? "Not available."
                : executiveSummary;

            var contextBlock =
                $@"

[CONTEXT]
Project Name: {job.ProjectName}
Project Address: {job.JobAddress?.FormattedAddress ?? "Not Specified"}
Project Location: {job.JobAddress?.City ?? "Not Specified"}, {job.JobAddress?.State ?? "Not Specified"}, {job.JobAddress?.Country ?? "Not Specified"}
General Contractor: {gc.CompanyName ?? $"{gc.FirstName} {gc.LastName}"}
General Contractor Address: {gcAddress?.FormattedAddress ?? "Not Specified"}
Client Name: {clientName}
Client Email: {clientEmail}
Contract Date: {DateTime.UtcNow:yyyy-MM-dd}
Executive Summary Context:
{executiveSummaryContext}
";

            var populatedPrompt = $"{prompt}\n{contextBlock}";

            var (contractText, _) = await _aiService.StartTextConversationAsync(
                "system-user",
                "You are an AI assistant with expert-level knowledge in international construction law and contract drafting.",
                populatedPrompt
            );

            var finalContractText = string.IsNullOrWhiteSpace(contractText)
                ? "Contract draft could not be generated."
                : contractText;

            finalContractText = SanitizeGeneratedContractText(finalContractText);

            var utcNow = DateTime.UtcNow;
            var contract = await _context.Contracts.FirstOrDefaultAsync(c =>
                c.JobId == jobId
                && (
                    c.ContractType == "GC_CLIENT"
                    || (c.ContractType == null && c.GcId == gcId && c.ScVendorId == gcId)
                )
            );

            if (contract == null)
            {
                contract = new Contract
                {
                    JobId = jobId,
                    GcId = gcId,
                    ScVendorId = gcId,
                    CreatedAt = utcNow,
                };

                _context.Contracts.Add(contract);
            }

            contract.GcId = gcId;
            contract.ScVendorId = gcId;
            contract.ContractText = finalContractText;
            contract.GcSignature = string.Empty;
            contract.ScVendorSignature = string.Empty;
            contract.Status = "GENERATED";
            contract.ContractType = "GC_CLIENT";
            contract.ClientName = clientName;
            contract.ClientEmail = clientEmail;
            contract.FileUrl = null;
            contract.FileName = null;
            contract.FileContentType = null;
            contract.GeneratedPromptFile = "general-contractor-client-contract-prompt.txt";
            contract.UpdatedAt = utcNow;

            await _context.SaveChangesAsync();

            return contract;
        }

        private static string SanitizeGeneratedContractText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var sanitized = text.Replace("\r\n", "\n").Replace('\r', '\n');

            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized,
                @"^\s*Reference:\s*Ready\s+for\s+the\s+next\s+prompt\s*\[[^\]]+\]\.?\s*$",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.Multiline
            );

            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized,
                @"^\s*Ready\s+for\s+the\s+next\s+prompt\s*\d+\.?\s*$",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.Multiline
            );

            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized,
                @"^\s*\[MANDATORY\s+FOOTER\]\s*$",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.Multiline
            );

            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized,
                @"Reference:\s*Ready\s+for\s+the\s+next\s+prompt\s*(\[[^\]]+\]|\d+)\.?",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.Multiline
            );

            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized,
                @"Ready\s+for\s+the\s+next\s+prompt\s*(\[[^\]]+\]|\d+)\.?",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.Multiline
            );

            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized,
                @"\[MANDATORY\s+FOOTER\]",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.Multiline
            );

            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized,
                @"^\s*Reference:\s*$",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    | System.Text.RegularExpressions.RegexOptions.Multiline
            );

            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\n{3,}", "\n\n");
            return sanitized.Trim();
        }

        private static string ExtractExecutiveSummary(string? fullResponse)
        {
            if (string.IsNullOrWhiteSpace(fullResponse))
            {
                return string.Empty;
            }

            var cleanResponse = System.Text.RegularExpressions.Regex
                .Replace(fullResponse, @"```json[\s\S]*?```", string.Empty)
                .Trim();

            var startMarkerRegex = new System.Text.RegularExpressions.Regex(
                @"###?\s*Executive Summary",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            var match = startMarkerRegex.Match(cleanResponse);
            if (!match.Success)
            {
                return cleanResponse;
            }

            var content = cleanResponse.Substring(match.Index);
            var endMarker = "Executive Summary Complete.";
            var endIndex = content.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
            if (endIndex >= 0)
            {
                content = content.Substring(0, endIndex);
            }

            content = startMarkerRegex.Replace(content, string.Empty).Trim();
            return string.IsNullOrWhiteSpace(content) ? string.Empty : content;
        }

        public async Task<Contract> UploadClientContractAsync(
            Guid contractId,
            IFormFile file,
            string? gcId
        )
        {
            var contract = await _context.Contracts.FirstOrDefaultAsync(c => c.Id == contractId);
            if (contract == null)
            {
                throw new KeyNotFoundException("Contract not found.");
            }

            if (
                !string.IsNullOrWhiteSpace(gcId)
                && !string.Equals(contract.GcId, gcId, StringComparison.Ordinal)
            )
            {
                throw new UnauthorizedAccessException(
                    "You are not allowed to upload files for this contract."
                );
            }

            if (file.Length == 0)
            {
                throw new InvalidOperationException("Uploaded file is empty.");
            }

            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only PDF files are supported.");
            }

            await using var stream = file.OpenReadStream();
            var uploadedUrl = await _azureBlobService.UploadFileAsync(
                file.FileName,
                stream,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/pdf" : file.ContentType,
                contract.JobId
            );

            contract.FileUrl = uploadedUrl;
            contract.FileName = file.FileName;
            contract.FileContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/pdf"
                : file.ContentType;
            contract.ContractType ??= "GC_CLIENT";
            contract.Status = "UPLOADED";
            contract.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return contract;
        }

        public async Task<(
            byte[] Content,
            string ContentType,
            string FileName
        )> DownloadContractFileAsync(Guid contractId)
        {
            var contract = await _context
                .Contracts.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == contractId);
            if (contract == null)
            {
                throw new KeyNotFoundException("Contract not found.");
            }

            if (string.IsNullOrWhiteSpace(contract.FileUrl))
            {
                throw new FileNotFoundException("No contract PDF is available for download.");
            }

            var (content, contentType) = await _azureBlobService.DownloadBlobAsBytesAsync(
                contract.FileUrl
            );
            var fileName = string.IsNullOrWhiteSpace(contract.FileName)
                ? $"contract-{contract.Id}.pdf"
                : contract.FileName;

            return (
                content,
                string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType,
                fileName
            );
        }

        private static byte[] BuildContractPdfBytes(string contractText, string projectName)
        {
            using var output = new MemoryStream();
            using var writer = new PdfWriter(output);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf);

            var plainText = ToPlainContractText(contractText);

            document.Add(new Paragraph($"Construction Contract - {projectName}").SetFontSize(16));
            document.Add(
                new Paragraph($"Generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").SetFontSize(
                    10
                )
            );
            document.Add(new Paragraph(" "));

            foreach (var line in plainText.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    document.Add(new Paragraph(" "));
                    continue;
                }

                document.Add(new Paragraph(line));
            }

            document.Close();
            return output.ToArray();
        }

        private static string ToPlainContractText(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return string.Empty;
            }

            var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');

            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"^\s{0,3}(#{1,6})\s*",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.Multiline
            );

            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.*?)\*\*", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"__(.*?)__", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*(.*?)\*", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"_(.*?)_", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"~~(.*?)~~", "$1");

            text = System.Text.RegularExpressions.Regex.Replace(text, @"`([^`]*)`", "$1");

            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"\[([^\]]+)\]\(([^\)]+)\)",
                "$1 ($2)"
            );

            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"^\s*[-*+]\s+",
                "• ",
                System.Text.RegularExpressions.RegexOptions.Multiline
            );

            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"^\s*\d+\.\s+",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.Multiline
            );

            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"^\s*>\s?",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.Multiline
            );

            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"^\s*---+\s*$",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.Multiline
            );

            text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "Project" : cleaned;
        }

        public async Task<Contract> GenerateContractAsync(int jobId, string gcId, string scVendorId)
        {
            var job = await _context
                .Jobs.Include(j => j.JobAddress)
                .FirstOrDefaultAsync(j => j.Id == jobId);

            var gc = await _context.Users.FindAsync(gcId);
            var scVendor = await _context.Users.FindAsync(scVendorId);
            var winningBid = await _context.Bids.FirstOrDefaultAsync(b =>
                b.JobId == jobId && b.UserId == scVendorId
            );

            var gcAddress = await _context.UserAddress.FirstOrDefaultAsync(ua => ua.UserId == gcId);
            var scVendorAddress = await _context.UserAddress.FirstOrDefaultAsync(ua =>
                ua.UserId == scVendorId
            );

            if (job == null || gc == null || scVendor == null || winningBid == null)
            {
                throw new Exception(
                    "Could not find all required information to generate the contract."
                );
            }

            var prompt = await _promptManager.GetPromptAsync(
                null,
                "subcontractor-agreement-prompt.txt"
            );

            var bidAnalysis = await _context.BidAnalyses.FirstOrDefaultAsync(ba =>
                ba.BidId == winningBid.Id
            );
            var analysisResult = bidAnalysis?.AnalysisResult ?? "No analysis found.";

            var populatedPrompt = prompt
                .Replace("[e.g., Canada]", job.JobAddress?.Country ?? "Not Specified")
                .Replace("[e.g., Ontario]", job.JobAddress?.State ?? "Not Specified")
                .Replace("[e.g., Toronto]", job.JobAddress?.City ?? "Not Specified")
                .Replace("[e.g., The Maple Leaf Tower Commercial Fit-Out]", job.ProjectName)
                .Replace(
                    "[Paste the detailed output from the previous AI analysis here. This should include the full scope of work, materials list, labor and equipment breakdown, exclusions, assumptions, and the agreed-upon price.]",
                    analysisResult
                )
                .Replace(
                    "[Your Company Name Inc./Ltd./etc.]",
                    gc.CompanyName ?? $"{gc.FirstName} {gc.LastName}"
                )
                .Replace(
                    "[Your Company's Full Address]",
                    gcAddress?.FormattedAddress ?? "Not Specified"
                )
                .Replace(
                    "[Subcontractor's Full Legal Company Name]",
                    scVendor.CompanyName ?? $"{scVendor.FirstName} {scVendor.LastName}"
                )
                .Replace(
                    "[Subcontractor's Full Address]",
                    scVendorAddress?.FormattedAddress ?? "Not Specified"
                )
                .Replace("[e.g., CAD, USD, EUR, GBP]", "USD") // TODO: Setting as USD for now, need to check prompt output for currency
                .Replace("[Enter the total amount]", winningBid.Amount.ToString("F2"))
                .Replace(
                    "[e.g., Monthly progress claims, 30-day payment cycle, 10% statutory holdback/retention as per local law.]",
                    "Net 30"
                )
                .Replace("[Date]", DateTime.UtcNow.ToShortDateString())
                .Replace("[e.g., 6 Months]", "Not Specified");

            var (contractText, _) = await _aiService.StartTextConversationAsync(
                "system-user",
                "You are an AI assistant with expert-level knowledge in international construction law and contract drafting.",
                populatedPrompt
            );

            var contract = new Contract
            {
                JobId = jobId,
                GcId = gcId,
                ScVendorId = scVendorId,
                ContractText = contractText,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow,
            };

            _context.Contracts.Add(contract);
            await _context.SaveChangesAsync();

            return contract;
        }
    }
}
