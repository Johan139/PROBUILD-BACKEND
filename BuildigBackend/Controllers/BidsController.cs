using System.Security.Claims;
using System.Text.Json;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BuildigBackend.Interface;
using BuildigBackend.Models;
using BuildigBackend.Models.DTO;
using BuildigBackend.Services;
using System.IO.Compression;
using BuildigBackend.Helpers;

namespace BuildigBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BidsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IAiAnalysisService _aiAnalysisService;
        private readonly AzureBlobService _azureBlobService;
        private readonly SubscriptionService _subscriptionService;
        private readonly ILogger<BidsController> _logger;

        public BidsController(
            ApplicationDbContext context,
            IAiAnalysisService aiAnalysisService,
            AzureBlobService azureBlobService,
            SubscriptionService subscriptionService,
            ILogger<BidsController> logger
        )
        {
            _context = context;
            _aiAnalysisService = aiAnalysisService;
            _azureBlobService = azureBlobService;
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<BidModel>>> GetBids()
        {
            return await _context.Bids.Include(b => b.Job).Include(b => b.User).ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<BidModel>> GetBid(int id)
        {
            var bid = await _context
                .Bids.AsNoTracking()
                .Include(b => b.Job)
                .Include(b => b.User)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bid == null)
            {
                return NotFound();
            }

            if (!string.IsNullOrWhiteSpace(bid.DocumentUrl))
            {
                bid.DocumentUrl = Url.Action(
                    nameof(DownloadBidDocument),
                    "Bids",
                    new { id = bid.Id },
                    protocol: Request.Scheme
                );
            }

            return bid;
        }

        [HttpGet("{id}/document")]
        public async Task<IActionResult> DownloadBidDocument(int id)
        {
            var bid = await _context
                .Bids.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bid == null)
            {
                return NotFound("Bid not found.");
            }

            if (string.IsNullOrWhiteSpace(bid.DocumentUrl))
            {
                return NotFound("No quote document is available for this bid.");
            }

            try
            {
                var (contentStream, contentType, originalFileName) =
                    await _azureBlobService.GetBlobContentAsync(bid.DocumentUrl);

                if (contentType == "application/gzip")
                {
                    var decompressedStream = new MemoryStream();
                    using (var gzipStream = new GZipStream(contentStream, CompressionMode.Decompress))
                    {
                        await gzipStream.CopyToAsync(decompressedStream);
                    }
                    decompressedStream.Position = 0;

                    var inferredContentType = FileHelpers.GetContentTypeFromFileName(originalFileName);
                    return File(decompressedStream, inferredContentType, originalFileName);
                }

                return File(contentStream, contentType, originalFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download bid quote document for bidId {BidId}", id);
                return StatusCode(500, "Failed to download bid quote document.");
            }
        }

        [HttpGet("job/{jobId}")]
        public async Task<ActionResult<IEnumerable<object>>> GetBidsForJob(int jobId)
        {
            try
            {

     
            var bids = await _context
                .Bids.Where(b => b.JobId == jobId)
                .AsNoTracking()
                .Include(b => b.User)
                .ToListAsync();

            var userIds = bids
                .Select(b => b.UserId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            var companiesByOwnerId = await _context.Companies
                .AsNoTracking()
                .Where(c => userIds.Contains(c.OwnerUserId))
                .ToDictionaryAsync(c => c.OwnerUserId);

            var result = bids.Select(b =>
            {
                var u = b.User;
                var company = b.UserId != null && companiesByOwnerId.TryGetValue(b.UserId, out var found)
                    ? found
                    : null;

                var documentUrl = !string.IsNullOrWhiteSpace(b.DocumentUrl)
                    ? Url.Action(
                        nameof(DownloadBidDocument),
                        "Bids",
                        new { id = b.Id },
                        protocol: Request.Scheme
                    )
                    : null;

                var first = u?.FirstName ?? string.Empty;
                var last = u?.LastName ?? string.Empty;
                var fullName = string.Join(" ", new[] { first, last }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

                var locationParts = new[] { u?.City, u?.State, u?.Country }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .ToArray();
                var location = locationParts.Length > 0 ? string.Join(", ", locationParts) : "N/A";

                var buildigRating = u?.ProbuildRating ?? 0;
                var googleRating = u?.GoogleRating ?? 0;

                var companyName =
                    !string.IsNullOrWhiteSpace(u?.CompanyName)
                        ? u!.CompanyName!
                        : (!string.IsNullOrWhiteSpace(company?.Name)
                            ? company!.Name!
                            : fullName);

                var email = !string.IsNullOrWhiteSpace(u?.Email)
                    ? u!.Email!
                    : (company?.Email ?? "N/A");
                var phone = !string.IsNullOrWhiteSpace(u?.PhoneNumber)
                    ? u!.PhoneNumber!
                    : (company?.PhoneNumber ?? "N/A");

                var yearsText = !string.IsNullOrWhiteSpace(u?.YearsOfOperation)
                    ? u!.YearsOfOperation
                    : company?.YearsOfOperation;

                var yearsParsed = 0;
                if (!string.IsNullOrWhiteSpace(yearsText))
                {
                    int.TryParse(yearsText.Trim(), out yearsParsed);
                }

                var specialty = !string.IsNullOrWhiteSpace(u?.Trade)
                    ? u!.Trade!
                    : (company?.Trade ?? "General Trade");

                var licenseNo = !string.IsNullOrWhiteSpace(u?.CompanyRegNo)
                    ? u!.CompanyRegNo!
                    : (company?.CompanyRegNo ?? "N/A");

                return new
                {
                    id = b.Id,
                    userId = b.UserId,
                    amount = b.Amount,
                    inclusions = b.Inclusions,
                    exclusions = b.Exclusions,
                    biddingRound = b.BiddingRound,
                    isFinalist = b.IsFinalist,
                    status = b.Status,
                    submittedAt = b.SubmittedAt,
                    jobId = b.JobId,
                    task = b.Task,
                    duration = b.Duration,
                    documentUrl,
                    quoteId = b.QuoteId,
                    tradePackageId = b.TradePackageId,

                    companyName,
                    contact = !string.IsNullOrWhiteSpace(fullName) ? fullName : "N/A",
                    phone,
                    email,
                    yearsInBusiness = yearsParsed,
                    specialty,
                    location,

                    licenseNo,

                    rating = buildigRating,
                    buildIgRating = buildigRating,
                    buildIgReviews = 0,
                    googleRating,
                    googleReviews = 0,
                };
            }).ToList();

            return Ok(result);
            }
            catch (Exception)
            {

                throw;
            }
        }

        [HttpPost("upload")]
        public async Task<ActionResult<BidModel>> PostPdfBid([FromBody] PdfBidDto bidRequest)
        {
            var userId = User.FindFirstValue("UserId");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var bid = new BidModel
            {
                JobId = bidRequest.JobId,
                TradePackageId = bidRequest.TradePackageId,
                DocumentUrl = bidRequest.DocumentUrl,
                Amount = bidRequest.Amount ?? 0,
                Inclusions = bidRequest.Inclusions,
                Exclusions = bidRequest.Exclusions,
                QuoteId = bidRequest.QuoteId,
                UserId = userId,
                Status = "Submitted",
                SubmittedAt = DateTime.UtcNow,
            };

            if (bidRequest.QuoteId.HasValue)
            {
                var jobOwnerId = await _context
                    .Jobs.AsNoTracking()
                    .Where(j => j.Id == bidRequest.JobId)
                    .Select(j => j.UserId)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(jobOwnerId))
                {
                    var quote = await _context.Quotes.FirstOrDefaultAsync(q => q.Id == bidRequest.QuoteId.Value);
                    if (quote != null)
                    {
                        quote.SentTo = jobOwnerId;

                        if (quote.Status == "Draft")
                        {
                            var canSubmit = await _subscriptionService.CanSubmitQuote(quote.CreatedID);
                            if (!canSubmit)
                            {
                                return BadRequest("Quote submission limit reached.");
                            }

                            quote.Status = "Submitted";
                            await _subscriptionService.IncrementQuoteCount(quote.CreatedID);
                        }
                    }
                }
            }

            _context.Bids.Add(bid);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetBid), new { id = bid.Id }, bid);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutBid(int id, BidModel bid)
        {
            if (id != bid.Id)
            {
                return BadRequest();
            }

            _context.Entry(bid).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!BidExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBid(int id)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var bid = await _context.Bids.FindAsync(id);
            if (bid == null)
            {
                return NotFound();
            }

            if (bid.UserId != userId)
            {
                return Forbid();
            }

            _context.Bids.Remove(bid);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("{id}/withdraw")]
        public async Task<IActionResult> WithdrawBid(int id)
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var bid = await _context.Bids.FindAsync(id);
            if (bid == null)
            {
                return NotFound();
            }

            if (bid.UserId != userId)
            {
                return Forbid();
            }

            if (bid.Status != "Submitted")
            {
                return BadRequest("Only submitted bids can be withdrawn.");
            }

            bid.Status = "Withdrawn";
            await _context.SaveChangesAsync();

            return Ok(bid);
        }

        [HttpPost("award-trade-package-bid")]
        public async Task<IActionResult> AwardTradePackageBid(
            [FromBody] AwardTradePackageBidDto request
        )
        {
            if (request == null || request.JobId <= 0 || request.TradePackageId <= 0)
            {
                return BadRequest("jobId and tradePackageId are required.");
            }

            var tradePackage = await _context.TradePackages.FirstOrDefaultAsync(tp =>
                tp.Id == request.TradePackageId && tp.JobId == request.JobId
            );

            if (tradePackage == null)
            {
                return NotFound("Trade package not found for the provided job.");
            }

            BidModel? awardedBid = null;
            if (request.BidId.HasValue)
            {
                awardedBid = await _context.Bids.FirstOrDefaultAsync(b =>
                    b.Id == request.BidId.Value
                    && b.JobId == request.JobId
                    && b.TradePackageId == request.TradePackageId
                );

                if (awardedBid == null)
                {
                    return BadRequest("Invalid bid for the selected trade package.");
                }
            }

            var packageBids = await _context
                .Bids.Where(b =>
                    b.JobId == request.JobId && b.TradePackageId == request.TradePackageId
                )
                .ToListAsync();

            if (awardedBid != null)
            {
                tradePackage.AwardedBidId = awardedBid.Id;
                tradePackage.Status = tradePackage.IsInHouse ? "In House" : "Awarded";

                foreach (var bid in packageBids)
                {
                    bid.Status = bid.Id == awardedBid.Id ? "Awarded" : "Submitted";
                }
            }
            else
            {
                tradePackage.AwardedBidId = null;

                foreach (var bid in packageBids.Where(b => b.Status == "Awarded"))
                {
                    bid.Status = "Submitted";
                }
            }

            await _context.SaveChangesAsync();

            return Ok(
                new
                {
                    tradePackageId = tradePackage.Id,
                    awardedBidId = tradePackage.AwardedBidId,
                    status = tradePackage.Status,
                }
            );
        }

        [HttpPost("analyze-trade-package")]
        public async Task<IActionResult> AnalyzeTradePackage(
            [FromBody] AnalyzeTradePackageRequestDto request
        )
        {
            if (request == null || request.JobId <= 0 || request.TradePackageId <= 0)
            {
                return BadRequest("jobId and tradePackageId are required.");
            }

            var tradePackage = await _context.TradePackages.FirstOrDefaultAsync(tp =>
                tp.Id == request.TradePackageId && tp.JobId == request.JobId
            );

            if (tradePackage == null)
            {
                return NotFound("Trade package not found for the provided job.");
            }

            if (tradePackage.IsInHouse)
            {
                return BadRequest(
                    new
                    {
                        message = "In-house packages do not support bid analysis. Upload in-house quotation instead.",
                    }
                );
            }

            var bids = await _context
                .Bids.Include(b => b.User)
                .Where(b => b.JobId == request.JobId && b.TradePackageId == request.TradePackageId)
                .OrderBy(b => b.Amount)
                .ToListAsync();

            if (!bids.Any())
            {
                return NotFound("No bids found for this trade package.");
            }

            var analysisJson = await _aiAnalysisService.AnalyzeBidsAsync(
                bids,
                tradePackage.Category ?? "Trade",
                tradePackage
            );

            if (string.IsNullOrWhiteSpace(analysisJson))
            {
                return StatusCode(500, "Analysis service returned an empty result.");
            }

            foreach (var bid in bids)
            {
                _context.BidAnalyses.Add(
                    new BidAnalysis
                    {
                        JobId = request.JobId,
                        BidId = bid.Id,
                        AnalysisResult = analysisJson,
                        CreatedAt = DateTime.UtcNow,
                    }
                );
            }

            await _context.SaveChangesAsync();

            using var document = JsonDocument.Parse(analysisJson);
            var payload = document.RootElement.Clone();
            return Ok(payload);
        }

        [HttpPost("analyze-preview-bids")]
        public async Task<IActionResult> AnalyzePreviewBids(
            [FromBody] AnalyzePreviewBidsRequestDto request
        )
        {
            if (request?.Bids == null || request.Bids.Count == 0)
            {
                return BadRequest("At least one preview bid is required.");
            }

            var previewBids = request
                .Bids.Select(b => new BidModel
                {
                    Id = b.BidId,
                    Amount = b.Amount,
                    Status = string.IsNullOrWhiteSpace(b.Status) ? "Submitted" : b.Status,
                    User = new UserModel
                    {
                        ProbuildRating = b.BuildigRating,
                        GoogleRating = b.GoogleRating,
                    },
                })
                .ToList();

            TradePackage? previewTradePackage = null;
            if (request.TradePackage != null)
            {
                previewTradePackage = new TradePackage
                {
                    Id = request.TradePackage.Id ?? 0,
                    TradeName = request.TradePackage.TradeName ?? "Preview Trade Package",
                    Category = request.TradePackage.Category,
                    ScopeOfWork = request.TradePackage.ScopeOfWork,
                    CsiCode = request.TradePackage.CsiCode,
                    Budget = request.TradePackage.Budget ?? 0,
                    LaborBudget = request.TradePackage.LaborBudget ?? 0,
                    MaterialBudget = request.TradePackage.MaterialBudget ?? 0,
                    LaborType = request.TradePackage.LaborType,
                };
            }

            var analysisJson = await _aiAnalysisService.AnalyzeBidsAsync(
                previewBids,
                request.ComparisonType ?? "Trade",
                previewTradePackage
            );

            if (string.IsNullOrWhiteSpace(analysisJson))
            {
                return StatusCode(500, "Analysis service returned an empty result.");
            }

            using var document = JsonDocument.Parse(analysisJson);
            var payload = document.RootElement.Clone();
            return Ok(payload);
        }

        private bool BidExists(int id)
        {
            return _context.Bids.Any(e => e.Id == id);
        }
    }
}


