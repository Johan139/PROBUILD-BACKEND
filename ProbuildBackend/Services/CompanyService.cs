using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Interface;
using ProbuildBackend.Models;
using ProbuildBackend.Models.DTO;

namespace ProbuildBackend.Services
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
            var company = await _db.Companies.FirstOrDefaultAsync(m => m.OwnerUserId == userId);

            if (company == null)
                return null;

            // Load addresses
            var addresses = await _db
                .CompanyAddresses.Where(a => a.CompanyId == company.Id)
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
                DevelopmentType = company.DevelopmentType,
                OperatingRegion = company.OperatingRegion,
                ProductsOffered = company.ProductsOffered,
                NotificationRadiusMiles = company.NotificationRadiusMiles,
                JobPreferences = company.JobPreferences,
                DeliveryArea = company.DeliveryArea,
                DeliveryTime = company.DeliveryTime,
                Email = company.Email,
                PhoneNumber = company.PhoneNumber,
                CountryNumberCode = company.CountryNumberCode,
                DocumentHeaderStyle = company.DocumentHeaderStyle,
                DocumentPrimaryColor = company.DocumentPrimaryColor,
                DocumentSecondaryColor = company.DocumentSecondaryColor,
                DocumentTextColor = company.DocumentTextColor,
                DocumentGradientStart = company.DocumentGradientStart,
                DocumentGradientEnd = company.DocumentGradientEnd,
                DocumentGradientDirection = company.DocumentGradientDirection,
                DocumentLogoUploaded = company.DocumentLogoUploaded,
                DocumentLogoFileName = company.DocumentLogoFileName,
                DocumentShowBankDetails = company.DocumentShowBankDetails,
                DocumentCompanyName = company.DocumentCompanyName,
                DocumentCompanyAddress = company.DocumentCompanyAddress,
                DocumentCompanyPhone = company.DocumentCompanyPhone,
                DocumentCompanyEmail = company.DocumentCompanyEmail,
                DocumentTaxId = company.DocumentTaxId,
                DocumentQuoteNumberPrefix = company.DocumentQuoteNumberPrefix,
                DocumentInvoiceNumberPrefix = company.DocumentInvoiceNumberPrefix,
                DocumentPaymentTerms = company.DocumentPaymentTerms,
                DocumentFooterNote = company.DocumentFooterNote,
                DocumentBankName = company.DocumentBankName,
                DocumentBankAccount = company.DocumentBankAccount,
                DocumentBankRouting = company.DocumentBankRouting,
                MeasurementSystem = company.MeasurementSystem,
                TemperatureUnit = company.TemperatureUnit,
                AreaUnit = company.AreaUnit,
                VolumeUnit = company.VolumeUnit,
                BillingAddress =
                    billingAddress != null
                        ? new CompanyAddressDTO
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
                            GooglePlaceId = billingAddress.GooglePlaceId,
                        }
                        : null,
                PhysicalAddress =
                    physicalAddress != null
                        ? new CompanyAddressDTO
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
                            GooglePlaceId = physicalAddress.GooglePlaceId,
                        }
                        : null,
            };
        }

        public async Task<CompaniesModel> SaveCompanyProfileAsync(
            string ownerUserId,
            CompanyProfileDto dto
        )
        {
            Console.WriteLine(JsonSerializer.Serialize(dto));
            try
            {
                var company = await _db.Companies.FirstOrDefaultAsync(c =>
                    c.OwnerUserId == ownerUserId
                );

                if (company == null)
                {
                    // CREATE
                    company = new CompaniesModel
                    {
                        OwnerUserId = ownerUserId,
                        CreatedAt = DateTime.UtcNow,
                    };

                    _db.Companies.Add(company);
                }

                // UPDATE (shared for create & update)
                company.Name = dto.Name;
                company.CompanyRegNo = dto.CompanyRegNo;
                company.VatNo = dto.VatNo;
                company.ConstructionType =
                    dto.ConstructionType != null
                        ? string.Join(
                            ",",
                            dto.ConstructionType.Where(x => !string.IsNullOrWhiteSpace(x))
                        )
                        : null;
                company.NrEmployees = dto.NrEmployees;
                company.YearsOfOperation = dto.YearsOfOperation;
                company.CertificationStatus = dto.CertificationStatus;
                company.CertificationDocumentPath = dto.CertificationDocumentPath;
                company.Trade = dto.Trade;
                company.SupplierType = dto.SupplierType;
                company.DevelopmentType = dto.DevelopmentType;
                company.OperatingRegion = dto.OperatingRegion;
                company.CountryNumberCode = dto.CountryNumberCode;
                company.Email = dto.Email;
                company.PhoneNumber = dto.PhoneNumber;
                company.NotificationRadiusMiles = dto.NotificationRadiusMiles;
                company.DocumentHeaderStyle = dto.DocumentHeaderStyle;
                company.DocumentPrimaryColor = dto.DocumentPrimaryColor;
                company.DocumentSecondaryColor = dto.DocumentSecondaryColor;
                company.DocumentTextColor = dto.DocumentTextColor;
                company.DocumentGradientStart = dto.DocumentGradientStart;
                company.DocumentGradientEnd = dto.DocumentGradientEnd;
                company.DocumentGradientDirection = dto.DocumentGradientDirection;
                company.DocumentLogoUploaded = dto.DocumentLogoUploaded;
                company.DocumentLogoFileName = dto.DocumentLogoFileName;
                company.DocumentShowBankDetails = dto.DocumentShowBankDetails;
                company.DocumentCompanyName = dto.DocumentCompanyName;
                company.DocumentCompanyAddress = dto.DocumentCompanyAddress;
                company.DocumentCompanyPhone = dto.DocumentCompanyPhone;
                company.DocumentCompanyEmail = dto.DocumentCompanyEmail;
                company.DocumentTaxId = dto.DocumentTaxId;
                company.DocumentQuoteNumberPrefix = dto.DocumentQuoteNumberPrefix;
                company.DocumentInvoiceNumberPrefix = dto.DocumentInvoiceNumberPrefix;
                company.DocumentPaymentTerms = dto.DocumentPaymentTerms;
                company.DocumentFooterNote = dto.DocumentFooterNote;
                company.DocumentBankName = dto.DocumentBankName;
                company.DocumentBankAccount = dto.DocumentBankAccount;
                company.DocumentBankRouting = dto.DocumentBankRouting;
                company.MeasurementSystem = dto.MeasurementSystem;
                company.TemperatureUnit = dto.TemperatureUnit;
                company.AreaUnit = dto.AreaUnit;
                company.VolumeUnit = dto.VolumeUnit;
                company.ProductsOffered =
                    dto.ProductsOffered != null
                        ? string.Join(
                            ",",
                            dto.ProductsOffered.Where(x => !string.IsNullOrWhiteSpace(x))
                        )
                        : null;
                company.JobPreferences =
                    dto.JobPreferences != null
                        ? string.Join(
                            ",",
                            dto.JobPreferences.Where(x => !string.IsNullOrWhiteSpace(x))
                        )
                        : null;

                company.DeliveryArea =
                    dto.DeliveryArea != null
                        ? string.Join(
                            ",",
                            dto.DeliveryArea.Where(x => !string.IsNullOrWhiteSpace(x))
                        )
                        : null;
                company.DeliveryTime = dto.DeliveryTime;

                await _db.SaveChangesAsync();

                if (dto.BillingAddress != null)
                {
                    UpsertCompanyAddress(company.Id, "BILLING", dto.BillingAddress);
                }

                if (dto.PhysicalAddress != null)
                {
                    UpsertCompanyAddress(company.Id, "PHYSICAL", dto.PhysicalAddress);
                }
                await _db.SaveChangesAsync();

                return company;
            }
            catch (Exception)
            {
                throw;
            }
        }

        private void UpsertCompanyAddress(int companyId, string addressType, CompanyAddressDTO dto)
        {
            try
            {
                var address = _db.CompanyAddresses.FirstOrDefault(a =>
                    a.CompanyId == companyId && a.AddressType == addressType
                );

                if (address == null)
                {
                    address = new CompanyAddressModel
                    {
                        CompanyId = companyId,
                        AddressType = addressType,
                        CreatedAt = DateTime.UtcNow,
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
