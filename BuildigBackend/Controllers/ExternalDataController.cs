using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BuildigBackend.Interface;
using BuildigBackend.Models.DTO;

namespace BuildigBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ExternalDataController : ControllerBase
    {
        private readonly IApolloService _apolloService;

        public ExternalDataController(IApolloService apolloService)
        {
            _apolloService = apolloService;
        }

        [HttpPost("subcontractors/discover")]
        public async Task<
            ActionResult<List<ExternalCompanyWithContactsDto>>
        > DiscoverSubcontractors(
            [FromBody] SubcontractorDiscoveryRequestDto request,
            CancellationToken cancellationToken
        )
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var results = await _apolloService.DiscoverSubcontractorsAsync(
                request,
                cancellationToken
            );
            return Ok(results);
        }

        [HttpPost("general-contractors/enrich")]
        public async Task<ActionResult<ExternalCompanyWithContactsDto>> EnrichGeneralContractor(
            [FromBody] GeneralContractorEnrichRequestDto request,
            CancellationToken cancellationToken
        )
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _apolloService.EnrichGeneralContractorAsync(
                request,
                cancellationToken
            );
            if (result is null)
            {
                return NotFound(new { message = "No external company information found." });
            }

            return Ok(result);
        }
    }
}

