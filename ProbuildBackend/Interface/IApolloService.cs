using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Interface
{
    public interface IApolloService
    {
        Task<List<ExternalCompanyWithContactsDto>> DiscoverSubcontractorsAsync(
            SubcontractorDiscoveryRequestDto request,
            CancellationToken cancellationToken = default
        );

        Task<ExternalCompanyWithContactsDto?> EnrichGeneralContractorAsync(
            GeneralContractorEnrichRequestDto request,
            CancellationToken cancellationToken = default
        );
    }
}

