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
        public string? DevelopmentType { get; set; }
        public string? OperatingRegion { get; set; }
        public string? ProductsOffered { get; set; }
        public int? NotificationRadiusMiles { get; set; }
        public string? JobPreferences { get; set; }
        public string? DeliveryArea { get; set; }
        public string? DeliveryTime { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? CountryNumberCode { get; set; }

        public CompanyAddressDTO? BillingAddress { get; set; }
        public CompanyAddressDTO? PhysicalAddress { get; set; }
        public string? DocumentHeaderStyle { get; set; }
        public string? DocumentPrimaryColor { get; set; }
        public string? DocumentSecondaryColor { get; set; }
        public string? DocumentTextColor { get; set; }
        public string? DocumentGradientStart { get; set; }
        public string? DocumentGradientEnd { get; set; }
        public string? DocumentGradientDirection { get; set; }
        public bool? DocumentLogoUploaded { get; set; }
        public string? DocumentLogoFileName { get; set; }
        public bool? DocumentShowBankDetails { get; set; }
        public string? DocumentCompanyName { get; set; }
        public string? DocumentCompanyAddress { get; set; }
        public string? DocumentCompanyPhone { get; set; }
        public string? DocumentCompanyEmail { get; set; }
        public string? DocumentTaxId { get; set; }
        public string? DocumentQuoteNumberPrefix { get; set; }
        public string? DocumentInvoiceNumberPrefix { get; set; }
        public string? DocumentPaymentTerms { get; set; }
        public string? DocumentFooterNote { get; set; }
        public string? DocumentBankName { get; set; }
        public string? DocumentBankAccount { get; set; }
        public string? DocumentBankRouting { get; set; }
        public string? MeasurementSystem { get; set; }
        public string? TemperatureUnit { get; set; }
        public string? AreaUnit { get; set; }
        public string? VolumeUnit { get; set; }
    }
}
