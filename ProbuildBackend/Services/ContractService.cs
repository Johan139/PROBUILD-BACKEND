using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Interface;

namespace ProbuildBackend.Services
{
    public class ContractService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAiService _aiService;
        private readonly IPromptManagerService _promptManager;

        public ContractService(ApplicationDbContext context, IAiService aiService, IPromptManagerService promptManager)
        {
            _context = context;
            _aiService = aiService;
            _promptManager = promptManager;
        }

        public async Task<Contract> GenerateContractAsync(int jobId, string gcId, string scVendorId)
        {
            var job = await _context.Jobs
                .Include(j => j.JobAddress)
                .FirstOrDefaultAsync(j => j.Id == jobId);

            var gc = await _context.Users.FindAsync(gcId);
            var scVendor = await _context.Users.FindAsync(scVendorId);
            var winningBid = await _context.Bids.FirstOrDefaultAsync(b => b.JobId == jobId && b.UserId == scVendorId);
            
            var gcAddress = await _context.UserAddress.FirstOrDefaultAsync(ua => ua.UserId == gcId);
            var scVendorAddress = await _context.UserAddress.FirstOrDefaultAsync(ua => ua.UserId == scVendorId);


            if (job == null || gc == null || scVendor == null || winningBid == null)
            {
                throw new Exception("Could not find all required information to generate the contract.");
            }

            var prompt = await _promptManager.GetPromptAsync(null, "subcontractor-agreement-prompt.txt");

            var bidAnalysis = await _context.BidAnalyses.FirstOrDefaultAsync(ba => ba.BidId == winningBid.Id);
            var analysisResult = bidAnalysis?.AnalysisResult ?? "No analysis found.";

            var populatedPrompt = prompt
                .Replace("[e.g., Canada]", job.JobAddress?.Country ?? "Not Specified")
                .Replace("[e.g., Ontario]", job.JobAddress?.State ?? "Not Specified")
                .Replace("[e.g., Toronto]", job.JobAddress?.City ?? "Not Specified")
                .Replace("[e.g., The Maple Leaf Tower Commercial Fit-Out]", job.ProjectName)
                .Replace("[Paste the detailed output from the previous AI analysis here. This should include the full scope of work, materials list, labor and equipment breakdown, exclusions, assumptions, and the agreed-upon price.]", analysisResult)
                .Replace("[Your Company Name Inc./Ltd./etc.]", gc.CompanyName ?? $"{gc.FirstName} {gc.LastName}")
                .Replace("[Your Company's Full Address]", gcAddress?.FormattedAddress ?? "Not Specified")
                .Replace("[Subcontractor's Full Legal Company Name]", scVendor.CompanyName ?? $"{scVendor.FirstName} {scVendor.LastName}")
                .Replace("[Subcontractor's Full Address]", scVendorAddress?.FormattedAddress ?? "Not Specified")
                .Replace("[e.g., CAD, USD, EUR, GBP]", "USD") // TODO: Setting as USD for now, need to check prompt output for currency
                .Replace("[Enter the total amount]", winningBid.Amount.ToString("F2"))
                .Replace("[e.g., Monthly progress claims, 30-day payment cycle, 10% statutory holdback/retention as per local law.]", "Net 30")
                .Replace("[Date]", DateTime.UtcNow.ToShortDateString())
                .Replace("[e.g., 6 Months]", "Not Specified");

            var (contractText, _) = await _aiService.StartTextConversationAsync("system-user", "You are an AI assistant with expert-level knowledge in international construction law and contract drafting.", populatedPrompt);

            var contract = new Contract
            {
                JobId = jobId,
                GcId = gcId,
                ScVendorId = scVendorId,
                ContractText = contractText,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow
            };

            _context.Contracts.Add(contract);
            await _context.SaveChangesAsync();

            return contract;
        }
    }
}