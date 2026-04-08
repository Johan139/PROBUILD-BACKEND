namespace ProbuildBackend.Models.DTO
{
    public class CompanyProfileResponseDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? CompanyRegNo { get; set; }
        public string? VatNo { get; set; }
        public string? ConstructionType { get; set; }
        public string? NrEmployees { get; set; }
        public string? YearsOfOperation { get; set; }
        public string? CertificationStatus { get; set; }
        public string? CertificationDocumentPath { get; set; }
        public string? Trade { get; set; }
        public string? SupplierType { get; set; }
        public string? ProductsOffered { get; set; }
        public string? JobPreferences { get; set; }
        public string? DeliveryArea { get; set; }
        public string? DeliveryTime { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? CountryNumberCode { get; set; }

        public CompanyAddressDTO? BillingAddress { get; set; }
        public CompanyAddressDTO? PhysicalAddress { get; set; }
    }
}