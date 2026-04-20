using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BuildigBackend.Interface;
using BuildigBackend.Models;
using BuildigBackend.Models.DTO;
using System.Text.Json;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace BuildigBackend.Services
{
    public class CompanyService : ICompanyService
    {
        private readonly ApplicationDbContext _db;

        public CompanyService(ApplicationDbContext db)
        {
            _db = db;
        }
        public async Task<CompanyProfileResponseDto> GetProfileCompanyByUserId(string userId)
        {
            var company = await _db.Companies
                .FirstOrDefaultAsync(m => m.OwnerUserId == userId);

            if (company == null)
                return null;

            // Load addresses
            var addresses = await _db.CompanyAddresses
                .Where(a => a.CompanyId == company.Id)
                .ToListAsync();

            var billingAddress = addresses.FirstOrDefault(a => a.AddressType == "BILLING");
            var physicalAddress = addresses.FirstOrDefault(a => a.AddressType == "PHYSICAL");

            return new CompanyProfileResponseDto
            {
                Id = company.Id,
                Name = company.Name,
                CompanyRegNo = company.CompanyRegNo,
                VatNo = company.VatNo,
                ConstructionType = company.ConstructionType,
                NrEmployees = company.NrEmployees,
                YearsOfOperation = company.YearsOfOperation,
                CertificationStatus = company.CertificationStatus,
                CertificationDocumentPath = company.CertificationDocumentPath,
                Trade = company.Trade,
                SupplierType = company.SupplierType,
                ProductsOffered = company.ProductsOffered,
                JobPreferences = company.JobPreferences,
                DeliveryArea = company.DeliveryArea,
                DeliveryTime = company.DeliveryTime,
                Email=company.Email,
                PhoneNumber=company.PhoneNumber,
                CountryNumberCode=company.CountryNumberCode,
                BillingAddress = billingAddress != null ? new CompanyAddressDTO
                {
                    StreetNumber = billingAddress.StreetNumber,
                    StreetName = billingAddress.StreetName,
                    City = billingAddress.City,
                    State = billingAddress.State,
                    PostalCode = billingAddress.PostalCode,
                    Country = billingAddress.Country,
                    Latitude = billingAddress.Latitude,
                    Longitude = billingAddress.Longitude,
                    FormattedAddress = billingAddress.FormattedAddress,
                    GooglePlaceId = billingAddress.GooglePlaceId
                } : null,
                PhysicalAddress = physicalAddress != null ? new CompanyAddressDTO
                {
                    StreetNumber = physicalAddress.StreetNumber,
                    StreetName = physicalAddress.StreetName,
                    City = physicalAddress.City,
                    State = physicalAddress.State,
                    PostalCode = physicalAddress.PostalCode,
                    Country = physicalAddress.Country,
                    Latitude = physicalAddress.Latitude,
                    Longitude = physicalAddress.Longitude,
                    FormattedAddress = physicalAddress.FormattedAddress,
                    GooglePlaceId = physicalAddress.GooglePlaceId
                } : null
            };
        }
        public async Task<CompaniesModel> SaveCompanyProfileAsync(
            string ownerUserId,
         CompanyProfileDto dto)
        {
            Console.WriteLine(JsonSerializer.Serialize(dto));
            try
            {

            var company = await _db.Companies
                .FirstOrDefaultAsync(c => c.OwnerUserId == ownerUserId);


            if (company == null)
            {
                // 🔹 CREATE
                company = new CompaniesModel
                {
                    OwnerUserId = ownerUserId,
                    CreatedAt = DateTime.UtcNow
                };

                _db.Companies.Add(company);
            }

            // 🔹 UPDATE (shared for create & update)
            company.Name = dto.Name;
            company.CompanyRegNo = dto.CompanyRegNo;
            company.VatNo = dto.VatNo;
            company.ConstructionType = dto.ConstructionType != null
     ? string.Join(",", dto.ConstructionType.Where(x => !string.IsNullOrWhiteSpace(x)))
     : null;
            company.NrEmployees = dto.NrEmployees;
            company.YearsOfOperation = dto.YearsOfOperation;
            company.CertificationStatus = dto.CertificationStatus;
            company.CertificationDocumentPath = dto.CertificationDocumentPath;
            company.Trade = dto.Trade;
            company.SupplierType = dto.SupplierType;
                company.CountryNumberCode= dto.CountryNumberCode;
            company.Email = dto.Email;
            company.PhoneNumber = dto.PhoneNumber;
            company.ProductsOffered = dto.ProductsOffered != null
        ? string.Join(",", dto.ProductsOffered.Where(x => !string.IsNullOrWhiteSpace(x)))
        : null;
            company.JobPreferences = dto.JobPreferences != null
         ? string.Join(",", dto.JobPreferences.Where(x => !string.IsNullOrWhiteSpace(x)))
         : null;

            company.DeliveryArea = dto.DeliveryArea != null
                ? string.Join(",", dto.DeliveryArea.Where(x => !string.IsNullOrWhiteSpace(x)))
                : null;
            company.DeliveryTime = dto.DeliveryTime;

            await _db.SaveChangesAsync();


            if (dto.BillingAddress != null)
            {
                UpsertCompanyAddress(
                    company.Id,
                    "BILLING",
                    dto.BillingAddress
                );
            }


            if (dto.PhysicalAddress != null)
            {
                UpsertCompanyAddress(
                    company.Id,
                    "PHYSICAL",
                    dto.PhysicalAddress
                );
            }
            await _db.SaveChangesAsync();

            return company;
            }
            catch (Exception)
            {

                throw;
            }
        }
        private void UpsertCompanyAddress(
    int companyId,
    string addressType,
    CompanyAddressDTO dto
)
        {
            try
            {


            var address = _db.CompanyAddresses
                .FirstOrDefault(a =>
                    a.CompanyId == companyId &&
                    a.AddressType == addressType);

            if (address == null)
            {
                address = new CompanyAddressModel
                {
                    CompanyId = companyId,
                    AddressType = addressType,
                    CreatedAt = DateTime.UtcNow
                };

                _db.CompanyAddresses.Add(address);
            }

            address.StreetNumber = dto.StreetNumber;
            address.StreetName = dto.StreetName;
            address.City = dto.City;
            address.State = dto.State;
            address.PostalCode = dto.PostalCode;
            address.Country = dto.Country;
            address.Latitude = dto.Latitude;
            address.Longitude = dto.Longitude;
            address.FormattedAddress = dto.FormattedAddress;
            address.GooglePlaceId = dto.GooglePlaceId;
            address.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception)
            {

                throw;
            }
        }
    }
}

