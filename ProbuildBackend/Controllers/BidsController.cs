using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BidsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IAiAnalysisService _aiAnalysisService;

        public BidsController(ApplicationDbContext context, IAiAnalysisService aiAnalysisService)
        {
            _context = context;
            _aiAnalysisService = aiAnalysisService;
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
                .Bids.Include(b => b.Job)
                .Include(b => b.User)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bid == null)
            {
                return NotFound();
            }

            return bid;
        }

        [HttpGet("job/{jobId}")]
        public async Task<ActionResult<IEnumerable<BidModel>>> GetBidsForJob(int jobId)
        {
            return await _context
                .Bids.Where(b => b.JobId == jobId)
                .Include(b => b.User)
                .ToListAsync();
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
                UserId = userId,
                Status = "Submitted",
                SubmittedAt = DateTime.UtcNow,
            };

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
                        ProbuildRating = b.ProbuildRating,
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
