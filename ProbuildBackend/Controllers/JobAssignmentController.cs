using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Models;
using Microsoft.EntityFrameworkCore;

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
            var isTeamMember = await _context.TeamMembers.AnyAsync(tm => tm.Id == userId);

            List<JobModel> jobsToProcess;

            if (isTeamMember)
            {
                var assignedJobIds = await _context.JobAssignments
                    .Where(ja => ja.UserId == userId)
                    .Select(ja => ja.JobId)
                    .Distinct()
                    .ToListAsync();

                jobsToProcess = await _context.Jobs
                    .Where(j => assignedJobIds.Contains(j.Id))
                    .ToListAsync();
            }
            else
            {
                jobsToProcess = await _context.Jobs.Where(j => j.UserId == userId).ToListAsync();
            }

            if (!jobsToProcess.Any())
            {
                return Ok(new List<JobAssignmentDto>());
            }

            var assignedJobList = new List<JobAssignmentDto>();
            foreach (var job in jobsToProcess)
            {
                var jobAssignmentRow = await _context.JobAssignments.Where(u => u.JobId == job.Id).ToListAsync();
                if (jobAssignmentRow == null) continue;

                var assignedJob = new JobAssignmentDto
                {
                    Id = job.Id,
                    ProjectName = job.ProjectName,
                    Address = job.Address,
                    Status = job.Status,
                    Stories = job.Stories,
                    BuildingSize = job.BuildingSize,
                    JobUser = new List<JobUser>()
                };

                foreach (var assignment in jobAssignmentRow)
                {
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == assignment.UserId);
                    if (user != null)
                    {
                        assignedJob.JobUser.Add(new JobUser
                        {
                            Id = user.Id,
                            FirstName = user.FirstName,
                            LastName = user.LastName,
                            PhoneNumber = user.PhoneNumber,
                            JobRole = assignment.JobRole,
                            UserType = user.UserType
                        });
                    }
                    else
                    {
                        var teamMember = await _context.TeamMembers.FirstOrDefaultAsync(tm => tm.Id == assignment.UserId);
                        if (teamMember != null)
                        {
                            assignedJob.JobUser.Add(new JobUser
                            {
                                Id = teamMember.Id,
                                FirstName = teamMember.FirstName,
                                LastName = teamMember.LastName,
                                PhoneNumber = teamMember.PhoneNumber,
                                JobRole = assignment.JobRole,
                                UserType = teamMember.Role
                            });
                        }
                    }
                }
                assignedJobList.Add(assignedJob);
            }
            return Ok(assignedJobList);
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
