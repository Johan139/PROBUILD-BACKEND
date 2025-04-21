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
        public async Task<IActionResult> PostJobAssignment([FromBody] JobAssignment assignmentData)
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
                assignedJob.JobUser = new List<JobUser>();
                foreach (var User in jobAssignmentRow)
                {
                    var user = await _context.Users.Where(u => u.Id == User.UserId).FirstOrDefaultAsync();
                    if (user == null)
                    {
                        assignedJob.JobUser = new List<JobUser>();
                        JobUser emptyJobUser = new JobUser();
                        assignedJob.JobUser.Add(emptyJobUser);
                        continue;
                    }

                    JobUser jobUser = new JobUser
                    {
                        Id = user.Id,
                        FirstName = user.FirstName,
                        LastName = user.LastName,
                        PhoneNumber = user.PhoneNumber,
                        JobRole = User.JobRole,
                        UserType = user.UserType
                    };
                    assignedJob.JobUser.Add(jobUser);
                }

                assignedJobList.Add(assignedJob);
            }
            return assignedJobList;
        }

        [HttpPut]
        public async Task<IActionResult> DeleteJobAssignment([FromBody] JobAssignment jobAssignment)
        {
            var jobAssignmentRow = await _context.JobAssignments.Where(u => u.JobId == jobAssignment.JobId && u.UserId == jobAssignment.UserId).FirstOrDefaultAsync();
            if (jobAssignmentRow == null)
                return NotFound();

            _context.JobAssignments.Remove(jobAssignmentRow);
            await _context.SaveChangesAsync();

            return Ok(jobAssignment);
        }

        [HttpGet("GetUsers")]
        public async Task<ActionResult<IEnumerable<JobUser[]>>> GetAvailableUser(string id)
        {
            var users = await _context.Users.ToListAsync();
            if (users == null)
                return NotFound("No accounts found.");

            List<JobUser> linkedUserList = new List<JobUser>();

            foreach (var user in users)
            {
                JobUser jobUser = new JobUser();
                jobUser.Id = user.Id;
                jobUser.PhoneNumber = user.PhoneNumber;
                jobUser.LastName = user.LastName;
                jobUser.FirstName = user.FirstName;
                jobUser.UserType = user.UserType;

                linkedUserList.Add(jobUser);
            }
            return Ok(linkedUserList);
        }

        public IActionResult Index()
        {
            return View();
        }
    }
}
