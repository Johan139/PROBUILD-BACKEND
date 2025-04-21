using Elastic.Apm.Api;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProfileController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<UserModel> _userManager;

        public ProfileController(ApplicationDbContext context, UserManager<UserModel> userManager) {
            _context = context;
            _userManager = userManager;
        }

        [HttpGet("GetTest")]
        public async Task<ActionResult<IEnumerable<UserModel>>> GetTest()
        {
            return Ok();
        }

        [HttpGet("GetProfile/{id}")]
        public async Task<ActionResult<IEnumerable<UserModel>>> GetUserById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Id parameter cannot be null or empty.");

            var users = await _context.Users.Where(a => a.Id == id).ToListAsync();
            if (users == null || !users.Any())
                return NotFound("No users found with the specified id.");

            return Ok(users);
        }

        [HttpPut("Update")]
        public async Task<IActionResult> Update(RegisterDto model)
        {
            if (ModelState.IsValid)
            {
                var user = await _context.Users.Where(a => a.Id == model.Id).FirstOrDefaultAsync();
                user.Id = model.Id;
                user.UserName = model.Email;
                user.Email = model.Email;
                user.FirstName = model.FirstName;
                user.LastName = model.LastName;
                user.PhoneNumber = model.PhoneNumber;
                user.CompanyName = model.CompanyName;
                user.CompanyRegNo = model.CompanyRegNo;
                user.VatNo = model.VatNo;
                user.UserType = model.UserType;
                user.ConstructionType = model.ConstructionType;
                user.NrEmployees = model.NrEmployees;
                user.YearsOfOperation = model.YearsOfOperation;
                user.CertificationStatus = model.CertificationStatus;
                user.CertificationDocumentPath = model.CertificationDocumentPath;
                user.Availability = model.Availability;
                user.Trade = model.Trade;
                user.ProductsOffered = model.ProductsOffered;
                user.SupplierType = model.SupplierType;
                user.ProjectPreferences = model.ProjectPreferences;
                user.DeliveryArea = model.DeliveryArea;
                user.DeliveryTime = model.DeliveryTime;
                user.Country = model.Country;
                user.State = model.State;
                user.City = model.City;
                user.SubscriptionPackage = model.SubscriptionPackage;

                try
                {
                    var result = _context.SaveChangesAsync();
                    Console.WriteLine($"Profile ({user.Id}) updated successfully.");
                    return Ok(new { message = "Profile updated successfully." });
                }
                catch (Exception ex) {
                    return BadRequest(ex);
                }
            }
            return BadRequest(ModelState);
        }
    }
}
