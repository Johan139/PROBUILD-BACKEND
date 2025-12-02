using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProjectsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ProjectsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/Projects
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProjectModel>>> GetProjects()
        {
            return await _context.Projects.ToListAsync();
        }

        // GET: api/Projects/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ProjectModel>> GetProject(int id)
        {
            var project = await _context.Projects.FindAsync(id);

            if (project == null)
            {
                return NotFound();
            }

            return project;
        }

        // POST: api/Projects
        [HttpPost]
        public async Task<IActionResult> PostProject(ProjectDto projectrequest)
        {
            var project = new ProjectModel
            {
                ProjectName = projectrequest.ProjectName,
                ForemanId = projectrequest.ForemanId,
                ContractorId = projectrequest.ContractorId,
                JobType = projectrequest.JobType,
                Qty = projectrequest.Qty,
                DesiredStartDate = projectrequest.DesiredStartDate,
                WallStructure = projectrequest.WallStructure,
                WallStructureSubtask = projectrequest.WallStructureSubtask,
                WallStructureStatus = projectrequest.WallStructureStatus,
                SubContractorWallStructureId = projectrequest.SubContractorWallStructureId,
                WallInsulation = projectrequest.WallInsulation,
                WallInsulationSubtask = projectrequest.WallInsulationSubtask,
                WallInsulationStatus = projectrequest.WallInsulationStatus,
                SubContractorWallInsulationId = projectrequest.SubContractorWallInsulationId,
                RoofStructure = projectrequest.RoofStructure,
                RoofStructureSubtask = projectrequest.RoofStructureSubtask,
                RoofStructureStatus = projectrequest.RoofStructureStatus,
                SubContractorRoofTypeId = projectrequest.SubContractorRoofTypeId,
                RoofInsulation = projectrequest.RoofInsulation,
                RoofInsulationSubtask = projectrequest.RoofInsulationSubtask,
                RoofInsulationStatus = projectrequest.RoofInsulationStatus,
                SubContractorRoofInsulationId = projectrequest.SubContractorRoofInsulationId,
                Foundation = projectrequest.Foundation,
                FoundationSubtask = projectrequest.FoundationSubtask,
                FoundationStatus = projectrequest.FoundationStatus,
                SubContractorFoundationId = projectrequest.SubContractorFoundationId,
                Finishes = projectrequest.Finishes,
                FinishesSubtask = projectrequest.FinishesSubtask,
                FinishesStatus = projectrequest.FinishesStatus,
                SubContractorFinishesId = projectrequest.SubContractorFinishesId,
                ElectricalSupplyNeeds = projectrequest.ElectricalSupplyNeeds,
                ElectricalSupplyNeedsSubtask = projectrequest.ElectricalSupplyNeedsSubtask,
                ElectricalStatus = projectrequest.ElectricalStatus,
                SubContractorElectricalSupplyNeedsId =
                    projectrequest.SubContractorElectricalSupplyNeedsId,
                Stories = projectrequest.Stories,
                BuildingSize = projectrequest.BuildingSize,
                OperatingArea = projectrequest.OperatingArea,
                // Bids = projectrequest.Bids - removed 25/09/25 - don't think it's needed
            };

            var ForemanExists = await _context.Users.AnyAsync(u => u.Id == project.ForemanId);
            if (!ForemanExists)
            {
                return BadRequest($"User with ID {project.ForemanId} does not exist.");
            }

            var ContractorExists = await _context.Users.AnyAsync(u => u.Id == project.ContractorId);
            if (!ContractorExists)
            {
                return BadRequest($"User with ID {project.ContractorId} does not exist.");
            }

            var SubContractorWallStructureExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorWallStructureId
            );
            if (!SubContractorWallStructureExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorWallStructureId} does not exist."
                );
            }

            var SubContractorWallInsulationExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorWallInsulationId
            );
            if (!SubContractorWallInsulationExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorWallInsulationId} does not exist."
                );
            }

            var SubContractorRoofStructureExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorRoofStructureId
            );
            if (!SubContractorRoofStructureExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorRoofStructureId} does not exist."
                );
            }

            var SubContractorRoofTypeExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorRoofTypeId
            );
            if (!SubContractorRoofTypeExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorRoofTypeId} does not exist."
                );
            }

            var SubContractorRoofInsulationExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorRoofInsulationId
            );
            if (!SubContractorRoofInsulationExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorRoofInsulationId} does not exist."
                );
            }

            var SubContractorFoundationExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorFoundationId
            );
            if (!SubContractorFoundationExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorFoundationId} does not exist."
                );
            }

            var SubContractorFinishesExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorFinishesId
            );
            if (!SubContractorFinishesExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorFinishesId} does not exist."
                );
            }

            var SubContractorElectricalSupplyNeedsExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorElectricalSupplyNeedsId
            );
            if (!SubContractorElectricalSupplyNeedsExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorElectricalSupplyNeedsId} does not exist."
                );
            }

            _context.Projects.Add(project);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetProject", new { id = project.Id }, project);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutProject(int id, ProjectModel project)
        {
            if (id != project.Id)
            {
                return BadRequest(
                    "The Project ID in the URL does not match the ID in the provided data."
                );
            }

            var foremanExists = await _context.Users.AnyAsync(u => u.Id == project.ForemanId);
            if (!foremanExists)
            {
                return BadRequest($"User with ID {project.ForemanId} for Foreman does not exist.");
            }

            var contractorExists = await _context.Users.AnyAsync(u => u.Id == project.ContractorId);
            if (!contractorExists)
            {
                return BadRequest(
                    $"User with ID {project.ContractorId} for Contractor does not exist."
                );
            }

            var SubContractorWallStructureExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorWallStructureId
            );
            if (!SubContractorWallStructureExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorWallStructureId} for ContractorWallStructure does not exist."
                );
            }

            var SubContractorWallInsulationExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorWallInsulationId
            );
            if (!SubContractorWallInsulationExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorWallInsulationId} for SubContractorWallInsulation does not exist."
                );
            }

            var SubContractorRoofStructureExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorRoofStructureId
            );
            if (!SubContractorRoofStructureExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorRoofStructureId} for SubContractorRoofStructure does not exist."
                );
            }

            var SubContractorRoofTypeExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorRoofTypeId
            );
            if (!SubContractorRoofTypeExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorRoofTypeId} for SubContractorRoofType does not exist."
                );
            }

            var SubContractorRoofInsulationExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorRoofInsulationId
            );
            if (!SubContractorRoofInsulationExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorRoofInsulationId} for SubContractorRoofInsulation does not exist."
                );
            }

            var SubContractorFoundationExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorFoundationId
            );
            if (!SubContractorFoundationExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorFoundationId} for SubContractorFoundation does not exist."
                );
            }

            var SubContractorFinishesExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorFinishesId
            );
            if (!SubContractorFinishesExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorFinishesId} for SubContractorFinishes does not exist."
                );
            }

            var SubContractorElectricalSupplyNeedsExists = await _context.Users.AnyAsync(u =>
                u.Id == project.SubContractorElectricalSupplyNeedsId
            );
            if (!SubContractorElectricalSupplyNeedsExists)
            {
                return BadRequest(
                    $"User with ID {project.SubContractorElectricalSupplyNeedsId} for SubContractorElectricalSupplyNeeds does not exist."
                );
            }

            _context.Entry(project).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ProjectExists(id))
                {
                    return NotFound("The project does not exist.");
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/Projects/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProject(int id)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null)
            {
                return NotFound();
            }

            _context.Projects.Remove(project);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ProjectExists(int id)
        {
            return _context.Projects.Any(e => e.Id == id);
        }

        [HttpGet("{userId}")]
        public async Task<ActionResult<IEnumerable<ProjectModel>>> GetProjectsByUserId(
            string userId
        )
        {
            var projects = await _context
                .Projects.Where(project => project.UserId == userId)
                .ToListAsync();

            if (projects == null || !projects.Any())
            {
                return NotFound();
            }

            return Ok(projects);
        }
    }
}
