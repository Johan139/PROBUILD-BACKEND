using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models;
using ProbuildBackend.Services;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BiddingController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly JobNotificationService _jobNotificationService;

        private readonly ContractService _contractService;

        public BiddingController(
            ApplicationDbContext context,
            IHubContext<NotificationHub> hubContext,
            JobNotificationService jobNotificationService,
            ContractService contractService
        )
        {
            _context = context;
            _hubContext = hubContext;
            _jobNotificationService = jobNotificationService;
            _contractService = contractService;
        }

        [HttpPost("{jobId}/start")]
        public async Task<IActionResult> StartBidding(
            int jobId,
            [FromBody] StartBiddingDto startBiddingDto
        )
        {
            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                return NotFound();
            }

            job.Status = "BIDDING";
            job.BiddingType = startBiddingDto.BiddingType;
            job.RequiredSubcontractorTypes = startBiddingDto.RequiredSubcontractorTypes.ToList();

            await _context.SaveChangesAsync();

            await _jobNotificationService.NotifyUsersAboutNewJob(job);

            return Ok();
        }

        [HttpPost("{jobId}/select-finalists")]
        public async Task<IActionResult> SelectFinalists(
            int jobId,
            [FromBody] SelectFinalistsDto selectFinalistsDto
        )
        {
            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                return NotFound("Job not found.");
            }

            var bids = await _context
                .Bids.Where(b => b.JobId == jobId && selectFinalistsDto.BidIds.Contains(b.Id))
                .ToListAsync();

            if (bids.Count != selectFinalistsDto.BidIds.Length)
            {
                return BadRequest("One or more bid IDs are invalid.");
            }

            var finalistIds = new List<string>();
            foreach (var bid in bids)
            {
                bid.IsFinalist = true;
                finalistIds.Add(bid.UserId);
            }

            await _context.SaveChangesAsync();

            await SendNotificationAsync(
                jobId,
                $"Congratulations! You have been selected as a finalist for the job: {job.ProjectName}",
                finalistIds
            );

            return Ok();
        }

        [HttpPost("{jobId}/award")]
        public async Task<IActionResult> AwardJob(int jobId, [FromBody] AwardJobDto awardJobDto)
        {
            var job = await _context.Jobs.FindAsync(jobId);
            if (job == null)
            {
                return NotFound();
            }

            var winningBid = await _context.Bids.FindAsync(awardJobDto.BidId);
            if (winningBid == null || winningBid.JobId != jobId)
            {
                return BadRequest("Invalid bid ID.");
            }

            job.Status = "IN_PROGRESS";

            // Generate a contract
            await _contractService.GenerateContractAsync(jobId, job.UserId, winningBid.UserId);

            var contract = await _contractService.GenerateContractAsync(
                jobId,
                job.UserId,
                winningBid.UserId
            );

            await _context.SaveChangesAsync();

            await SendNotificationAsync(
                jobId,
                $"Congratulations! You have been awarded the job: {job.ProjectName}",
                new List<string> { winningBid.UserId }
            );

            return Ok(new { contractId = contract.Id });
        }

        [HttpPost("{jobId}/analyze-bids")]
        public async Task<IActionResult> AnalyzeBids(
            int jobId,
            [FromBody] AnalyzeBidsDto analyzeBidsDto,
            [FromServices] AiAnalysisService aiAnalysisService
        )
        {
            var bids = await _context
                .Bids.Include(b => b.User)
                .Where(b => b.JobId == jobId && analyzeBidsDto.BidIds.Contains(b.Id))
                .ToListAsync();

            if (bids.Count == 0)
            {
                return NotFound("No matching bids found for the given bid IDs.");
            }

            var userType = bids.FirstOrDefault()?.User?.UserType;
            string comparisonType = userType == "Vendor" ? "Vendor" : "Subcontractor";

            var analysisResult = await aiAnalysisService.AnalyzeBidsAsync(bids, comparisonType);

            foreach (var bid in bids)
            {
                var bidAnalysis = new BidAnalysis
                {
                    JobId = jobId,
                    BidId = bid.Id,
                    AnalysisResult = analysisResult,
                    CreatedAt = DateTime.UtcNow,
                };
                _context.BidAnalyses.Add(bidAnalysis);
            }
            await _context.SaveChangesAsync();

            return Ok(new { message = analysisResult });
        }

        [HttpPost("generate-feedback")]
        public async Task<IActionResult> GenerateFeedback(
            [FromBody] GenerateFeedbackDto dto,
            [FromServices] AiAnalysisService aiAnalysisService
        )
        {
            var unsuccessfulBid = await _context
                .Bids.Include(b => b.User)
                .Include(b => b.Job)
                .FirstOrDefaultAsync(b => b.Id == dto.UnsuccessfulBidId);

            var winningBid = await _context
                .Bids.Include(b => b.User)
                .Include(b => b.Job)
                .FirstOrDefaultAsync(b => b.Id == dto.WinningBidId);

            if (unsuccessfulBid == null || winningBid == null)
            {
                return NotFound("One or both bids could not be found.");
            }

            var feedback = await aiAnalysisService.GenerateFeedbackForUnsuccessfulBidderAsync(
                unsuccessfulBid,
                winningBid
            );

            return Ok(new { feedback });
        }

        private async Task SendNotificationAsync(
            int jobId,
            string message,
            List<string> recipientIds
        )
        {
            var senderId = User.FindFirstValue("UserId") ?? "system";

            var notification = new NotificationModel
            {
                Message = message,
                Timestamp = DateTime.UtcNow,
                JobId = jobId,
                SenderId = senderId,
                Recipients = recipientIds,
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            foreach (var recipientId in recipientIds)
            {
                await _hubContext
                    .Clients.User(recipientId)
                    .SendAsync("ReceiveNotification", notification);
            }
        }
    }

    public class StartBiddingDto
    {
        public string BiddingType { get; set; }
        public string[] RequiredSubcontractorTypes { get; set; }
    }

    public class SelectFinalistsDto
    {
        public int[] BidIds { get; set; }
    }

    public class AwardJobDto
    {
        public int BidId { get; set; }
    }

    public class AnalyzeBidsDto
    {
        public int[] BidIds { get; set; }
    }

    public class GenerateFeedbackDto
    {
        public int UnsuccessfulBidId { get; set; }
        public int WinningBidId { get; set; }
    }
}
