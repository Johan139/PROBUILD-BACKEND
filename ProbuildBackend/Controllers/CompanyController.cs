using Amazon.S3.Model;
using Elastic.Apm.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;
using ProbuildBackend.Services;
using System.Security.Claims;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    public class CompanyController : ControllerBase
    {
        public readonly ICompanyService _companyService;
        public CompanyController(ICompanyService companyService)
        {
            _companyService = companyService;
        }

        [Authorize]
        [HttpGet("GetProfileCompany/{userId}")]
        public async Task<IActionResult> GetCompanyProfile([FromRoute] string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest("User ID is required");

            var company = await _companyService.GetProfileCompanyByUserId(userId);

            if (company == null)
                return NotFound();

            return Ok(company);
        }
        [Authorize]
        [HttpPut("saveCompany/{userId}")]
        public async Task<IActionResult> UpdateCompanyProfile(
            [FromRoute] string userId,
            [FromBody] CompanyProfileDto dto)
        {

            if (dto == null)
                return BadRequest("DTO is null");

            var company = await _companyService
                .SaveCompanyProfileAsync(userId, dto);

            return Ok(company);
        }


    }
}
