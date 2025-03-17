using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;
using System.Net;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JobsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        private readonly AzureBlobService _azureBlobservice;
        public JobsController(ApplicationDbContext context, AzureBlobService azureBlobservice)
        {
            _context = context;
            _azureBlobservice = azureBlobservice;
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

            if (jobrequest.Blueprint != null)
            {
                 await _azureBlobservice.UploadFiles(jobrequest.Blueprint);
                // Read the blueprint file into a byte array

                var fileNames = jobrequest.Blueprint.Select(file => file.FileName).ToList();
                job.Blueprint = string.Join(", ", fileNames);

            }
            else
            {
                job.Blueprint = null;
            }

            _context.Jobs.Add(job);
            await _context.SaveChangesAsync();

            return Ok(job);
        }
        [HttpPost]
        [RequestSizeLimit(200 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage([FromForm] JobDto jobrequest)
        {
            return null;
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
}
