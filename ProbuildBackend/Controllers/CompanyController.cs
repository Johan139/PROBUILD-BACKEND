using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ProbuildBackend.Interface;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Controllers
{
    [Route("api/[controller]")]
    public class CompanyController : ControllerBase
    {
        public readonly ICompanyService _companyService;
        private readonly ILogger<CompanyController> _logger;

        public CompanyController(ICompanyService companyService, ILogger<CompanyController> logger)
        {
            _companyService = companyService;
            _logger = logger;
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
            [FromBody] CompanyProfileDto dto
        )
        {
            _logger.LogInformation(
                "[CompanyController] saveCompany hit for userId {UserId}. Content-Type: {ContentType}. ModelStateValid: {ModelStateValid}",
                userId,
                Request.ContentType,
                ModelState.IsValid
            );

            if (!ModelState.IsValid)
            {
                foreach (var key in ModelState.Keys)
                {
                    var entry = ModelState[key];
                    if (entry?.Errors.Count > 0)
                    {
                        _logger.LogError(
                            "[CompanyController] ModelState error for key '{Key}'. AttemptedValue: {AttemptedValue}. RawValue: {RawValue}. Errors: {Errors}",
                            key,
                            entry.AttemptedValue,
                            entry.RawValue,
                            string.Join(" | ", entry.Errors.Select(e => e.ErrorMessage))
                        );
                    }
                }
            }

            if (dto == null)
            {
                _logger.LogError(
                    "[CompanyController] saveCompany received null DTO for userId {UserId}",
                    userId
                );
                return BadRequest("DTO is null");
            }

            _logger.LogInformation(
                "[CompanyController] saveCompany payload summary for userId {UserId}: {Payload}",
                userId,
                JsonSerializer.Serialize(
                    new
                    {
                        dto.Name,
                        dto.Email,
                        dto.PhoneNumber,
                        dto.CountryNumberCode,
                        dto.MeasurementSystem,
                        dto.TemperatureUnit,
                        dto.AreaUnit,
                        dto.VolumeUnit,
                        ConstructionTypeCount = dto.ConstructionType?.Count,
                        ProductsOfferedCount = dto.ProductsOffered?.Count,
                        JobPreferencesCount = dto.JobPreferences?.Count,
                        DeliveryAreaCount = dto.DeliveryArea?.Count,
                        HasBillingAddress = dto.BillingAddress != null,
                        HasPhysicalAddress = dto.PhysicalAddress != null,
                    }
                )
            );

            try
            {
                var company = await _companyService.SaveCompanyProfileAsync(userId, dto);

                _logger.LogInformation(
                    "[CompanyController] saveCompany completed successfully for userId {UserId}. CompanyId: {CompanyId}",
                    userId,
                    company.Id
                );

                return Ok(company);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[CompanyController] saveCompany failed for userId {UserId}",
                    userId
                );
                throw;
            }
        }
    }
}
