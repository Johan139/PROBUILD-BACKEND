using BuildigBackend.Models;
using BuildigBackend.Models.DTO;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace BuildigBackend.Interface
{
    public interface ICompanyService
    {
        Task<CompaniesModel> SaveCompanyProfileAsync(
            string ownerUserId,
            CompanyProfileDto dto);
        Task<CompanyProfileResponseDto> GetProfileCompanyByUserId(string UserId);
    }
}

