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
            try
            {
                var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == assignmentData.JobId);
                if (job == null)
                {
                    return NotFound($"Job with ID {assignmentData.JobId} not found.");
                }

                UserModel user = await _context.Users.FirstOrDefaultAsync(u => u.Id == assignmentData.UserId);
                TeamMember teamMember = null;

                if (user == null)
                {
                    teamMember = await _context.TeamMembers.FirstOrDefaultAsync(tm => tm.Id == assignmentData.UserId);
                    if (teamMember == null)
                    {
                        return NotFound($"User or Team Member with ID {assignmentData.UserId} not found.");
                    }
                }

                var jobAssignment = new JobAssignmentModel
                {
                    UserId = assignmentData.UserId,
                    JobId = assignmentData.JobId,
                    JobRole = assignmentData.JobRole
                };

                _context.JobAssignments.Add(jobAssignment);
                await _context.SaveChangesAsync();

                var assignedJob = new JobAssignmentDto
                {
                    Id = job.Id,
                    ProjectName = job.ProjectName,
                    Address = job.Address,
                    Status = job.Status,
                    Stories = job.Stories,
                    BuildingSize = job.BuildingSize,
                    JobUser = new List<JobUser>
                    {
                        new JobUser
                        {
                            Id = user?.Id ?? teamMember.Id,
                            FirstName = user?.FirstName ?? teamMember.FirstName,
                            LastName = user?.LastName ?? teamMember.LastName,
                            PhoneNumber = user?.PhoneNumber ?? teamMember.PhoneNumber,
                            JobRole = assignmentData.JobRole,
                            UserType = user?.UserType ?? teamMember.Role
                        }
                    }
                };

                return Ok(assignedJob);
            }
            catch (DbUpdateException ex)
            {
                return BadRequest(new { message = "Failed to assign job. Check for duplicate assignments or invalid data.", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
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

        [HttpPost("DeleteAssignment")]
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

    }
}
