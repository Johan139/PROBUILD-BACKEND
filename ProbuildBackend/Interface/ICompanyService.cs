using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace ProbuildBackend.Interface
{
    public interface ICompanyService
    {
        Task<CompaniesModel> SaveCompanyProfileAsync(
            string ownerUserId,
            CompanyProfileDto dto);
        Task<CompanyProfileResponseDto> GetProfileCompanyByUserId(string UserId);
    }
}
