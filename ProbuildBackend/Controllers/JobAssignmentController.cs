using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Models;
using Microsoft.AspNetCore.SignalR;
using ProbuildBackend.Middleware;
using ProbuildBackend.Services;
using Microsoft.EntityFrameworkCore;
using Elastic.Apm.Api;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JobAssignmentController : Controller
    {
        private readonly ApplicationDbContext _context;
        public JobAssignmentController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> PostJobAssignment(JobAssignment assignmentData)
        {
            JobAssignmentDto assignedJob = new JobAssignmentDto();
            var jobAssignment = new JobAssignmentModel
            {
                UserId = assignmentData.UserId,
                JobId = assignmentData.JobId,
                JobRole = assignmentData.JobRole
            };

            _context.JobAssignments.Add(jobAssignment);
            try
            {
                await _context.SaveChangesAsync();

                var job = await _context.Jobs.Where(u => u.Id == assignmentData.JobId).FirstOrDefaultAsync();
                if (job == null)
                    return NotFound();
                var user = await _context.Users.Where(u => u.Id == assignmentData.UserId).FirstOrDefaultAsync();
                if (user == null)
                    return NotFound();

                assignedJob.Id = job.Id;
                assignedJob.ProjectName = job.ProjectName;
                assignedJob.Address = job.Address;
                assignedJob.Status = job.Status;
                assignedJob.Stories = job.Stories;
                assignedJob.BuildingSize = job.BuildingSize;
                assignedJob.Status = job.Status;
                assignedJob.JobUser = new List<JobUser>();
                JobUser jobUser = new JobUser();
                jobUser.Id = user.Id;
                jobUser.FirstName = user.FirstName;
                jobUser.LastName = user.LastName;
                jobUser.PhoneNumber = user.PhoneNumber;
                jobUser.JobRole = assignmentData.JobRole;
                jobUser.UserType = user.UserType;
                assignedJob.JobUser.Add(jobUser);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict("Duplicate link already exists.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex);
            }

            return Ok(assignedJob);
        }

        [HttpGet("GetAssignedUsers/{userId}")]
        public async Task<ActionResult<List<JobAssignmentDto>>> GetAllJobAssignment(string userId)
        {
            List<JobAssignmentDto> assignedJobList = new List<JobAssignmentDto>();
            var jobList = await _context.Jobs.Where(u => u.UserId == userId).ToListAsync();
            if (jobList == null)
                return NotFound();

            foreach (var job in jobList)
            {
                var jobAssignmentRow = await _context.JobAssignments.Where(u => u.JobId == job.Id).ToListAsync();
                if (jobAssignmentRow == null)
                    return NotFound();

                JobAssignmentDto assignedJob = new JobAssignmentDto();
                assignedJob.Id = job.Id;
                assignedJob.ProjectName = job.ProjectName;
                assignedJob.Address = job.Address;
                assignedJob.Status = job.Status;
                assignedJob.Stories = job.Stories;
                assignedJob.BuildingSize = job.BuildingSize;
                assignedJob.Status = job.Status;

                foreach (var User in jobAssignmentRow)
                {
                    var user = await _context.Users.Where(u => u.Id == User.UserId).FirstOrDefaultAsync();
                    if (user == null)
                        continue;

                    assignedJob.JobUser = new List<JobUser>();
                    JobUser jobUser = new JobUser();
                    jobUser.Id = user.Id;
                    jobUser.FirstName = user.FirstName;
                    jobUser.LastName = user.LastName;
                    jobUser.PhoneNumber = user.PhoneNumber;
                    jobUser.JobRole = User.JobRole;
                    jobUser.UserType = user.UserType;
                    assignedJob.JobUser.Add(jobUser);
                }

                assignedJobList.Add(assignedJob);
            }
            return assignedJobList;
        }

        [HttpPut]
        public async Task<IActionResult> DeleteJobAssignment(JobAssignmentDto jobAssignment)
        {
            var jobAssignmentRow = await _context.JobAssignments.Where(u => u.JobId == jobAssignment.Id && u.UserId == jobAssignment.JobUser[0].Id).FirstOrDefaultAsync();
            if (jobAssignmentRow == null)
                return NotFound();

            _context.JobAssignments.Remove(jobAssignmentRow);
            await _context.SaveChangesAsync();

            return Ok(jobAssignment);
        }

        public IActionResult Index()
        {
            return View();
        }
    }
}
