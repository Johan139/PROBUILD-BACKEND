using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;
using System.Net;
using static Google.Apis.Requests.BatchRequest;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JobsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ProgressHub> _hubContext; // Inject IHubContext
        private readonly AzureBlobService _azureBlobservice;
        private readonly IHttpContextAccessor _httpContextAccessor;
        public JobsController(ApplicationDbContext context, AzureBlobService azureBlobservice, IHubContext<ProgressHub> hubContext, IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _context = context;
            _azureBlobservice = azureBlobservice;
            _hubContext = hubContext;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Models.JobModel>>> GetJobs()
        {
            return await _context.Jobs.ToListAsync();
        }

        [HttpGet("Id/{id}")]
        public async Task<ActionResult<Models.JobModel>> GetJob(int id)
        {
            var job = await _context.Jobs.FindAsync(id);

            if (job == null)
            {
                return NotFound();
            }

            return job;
        }

        [HttpPost]
        [RequestSizeLimit(200 * 1024 * 1024)]
        public async Task<IActionResult> PostJob([FromForm] JobDto jobrequest)
        {
            var job = new JobModel
            {
                ProjectName = jobrequest.ProjectName,
                JobType = jobrequest.JobType,
                Qty = jobrequest.Qty,
                DesiredStartDate = jobrequest.DesiredStartDate,
                WallStructure = jobrequest.WallStructure,
                WallStructureSubtask = jobrequest.WallStructureSubtask,
                WallInsulation = jobrequest.WallInsulation,
                WallInsulationSubtask = jobrequest.WallInsulationSubtask,
                RoofStructure = jobrequest.RoofStructure,
                RoofStructureSubtask = jobrequest.RoofStructureSubtask,
                RoofTypeSubtask = jobrequest.RoofTypeSubtask,
                RoofInsulation = jobrequest.RoofInsulation,
                Foundation = jobrequest.Foundation,
                FoundationSubtask = jobrequest.FoundationSubtask,
                Finishes = jobrequest.Finishes,
                Address = jobrequest.Address,
                FinishesSubtask = jobrequest.FinishesSubtask,
                ElectricalSupplyNeeds = jobrequest.ElectricalSupplyNeeds,
                ElectricalSupplyNeedsSubtask = jobrequest.ElectricalSupplyNeedsSubtask,
                Stories = jobrequest.Stories,
                BuildingSize = jobrequest.BuildingSize,
                OperatingArea = jobrequest.OperatingArea,
                UserId = jobrequest.UserId,
                Status = jobrequest.Status
            };

            _context.Jobs.Add(job);
            await _context.SaveChangesAsync();

            return Ok(job);
        }

        [HttpPost("UploadImage")]
        [RequestSizeLimit(200 * 1024 * 1024)] // 200MB
        public async Task<IActionResult> UploadImage([FromForm] UploadDocumentDTO jobRequest)
        {
            try
            {
                if (jobRequest == null)
                {
                    return BadRequest(new { error = "Invalid job request" });
                }

                if (jobRequest.Blueprint == null || !jobRequest.Blueprint.Any())
                {
                    return BadRequest(new { error = "No blueprint files provided" });
                }

                // Validate files
                var allowedExtensions = new[] { ".pdf", ".png", ".jpg", ".jpeg" };
                var uploadedFileUrls = new List<string>();

                foreach (var file in jobRequest.Blueprint)
                {
                    if (file.Length == 0)
                    {
                        return BadRequest(new { error = $"Empty file detected: {file.FileName}" });
                    }

                    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (!allowedExtensions.Contains(extension))
                    {
                        return BadRequest(new { error = $"Invalid file type: {file.FileName}" });
                    }
                }

                // Get connectionId from form data
                string connectionId = jobRequest.connectionId ?? _httpContextAccessor.HttpContext?.Connection.Id
                    ?? throw new InvalidOperationException("No valid connectionId provided.");

                Console.WriteLine($"Received connectionId from client: {connectionId}");

                // Upload files using the service and pass the client-provided connectionId
                uploadedFileUrls = await _azureBlobservice.UploadFiles(jobRequest.Blueprint, _hubContext, connectionId);

                var response = new UploadDocumentModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Status = "Uploaded",
                    FileUrls = uploadedFileUrls,
                    FileNames = jobRequest.Blueprint.Select(f => f.FileName).ToList(),
                    Message = $"Successfully uploaded {jobRequest.Blueprint.Count} file(s)"
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to upload files", details = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutJob(int id, [FromBody] JobModel job)
        {
            Console.WriteLine(job.Id);
            if (id != job.Id)
            {
                return BadRequest();
            }

            _context.Entry(job).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!JobExists(id))
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

        [HttpPost("DeleteTemporaryFiles")]
        public async Task<IActionResult> DeleteTemporaryFiles([FromBody] DeleteTemporaryFilesRequest request)
        {
            await _azureBlobservice.DeleteTemporaryFiles(request.BlobUrls);
            return Ok();
        }

        [HttpGet("userId/{userId}")]
        public async Task<ActionResult<IEnumerable<Models.JobModel>>> GetJobsByUserId(string userId)
        {
            var jobs = await _context.Jobs.Where(job => job.UserId == userId).ToListAsync();

            if (jobs == null || !jobs.Any())
            {
                return NotFound();
            }

            return Ok(jobs);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteJob(int id)
        {
            var job = await _context.Jobs.FindAsync(id);
            if (job == null)
            {
                return NotFound();
            }

            _context.Jobs.Remove(job);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool JobExists(int id)
        {
            return _context.Jobs.Any(e => e.Id == id);
        }
    }
    public class DeleteTemporaryFilesRequest
    {
        public List<string> BlobUrls { get; set; }
    }
}