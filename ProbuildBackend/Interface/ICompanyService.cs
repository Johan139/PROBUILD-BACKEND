using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Interface
{
    public interface ICompanyService
    {
        Task<CompaniesModel> SaveCompanyProfileAsync(string ownerUserId, CompanyProfileDto dto);
        Task<CompanyProfileResponseDto> GetProfileCompanyByUserId(string UserId);
    }
}
