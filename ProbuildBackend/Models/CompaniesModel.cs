using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class CompaniesModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string OwnerUserId { get; set; } = null!;

        [MaxLength(255)]
        public string? Name { get; set; }

        [MaxLength(50)]
        public string? SubscriptionTier { get; set; }

        public DateTime? SubscriptionValidUntil { get; set; }

        [MaxLength(100)]
        public string? StripeSubscriptionId { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        [MaxLength(255)]
        public string? CompanyRegNo { get; set; }

        [MaxLength(255)]
        public string? VatNo { get; set; }

        [MaxLength(255)]
        public string? ConstructionType { get; set; }

        [MaxLength(255)]
        public string? NrEmployees { get; set; }

        [MaxLength(255)]
        public string? YearsOfOperation { get; set; }

        [MaxLength(255)]
        public string? CertificationStatus { get; set; }

        public string? CertificationDocumentPath { get; set; }

        [MaxLength(255)]
        public string? Trade { get; set; }

        [MaxLength(255)]
        public string? SupplierType { get; set; }

        [MaxLength(255)]
        public string? DevelopmentType { get; set; }

        [MaxLength(255)]
        public string? OperatingRegion { get; set; }

        public string? ProductsOffered { get; set; }

        public int? NotificationRadiusMiles { get; set; }

        public string? JobPreferences { get; set; }

        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? CountryNumberCode { get; set; }

        public string? DeliveryArea { get; set; }

        [MaxLength(255)]
        public string? DeliveryTime { get; set; }

        [MaxLength(50)]
        public string? DocumentHeaderStyle { get; set; }

        [MaxLength(7)]
        public string? DocumentPrimaryColor { get; set; }

        [MaxLength(7)]
        public string? DocumentSecondaryColor { get; set; }

        [MaxLength(7)]
        public string? DocumentTextColor { get; set; }

        [MaxLength(7)]
        public string? DocumentGradientStart { get; set; }

        [MaxLength(7)]
        public string? DocumentGradientEnd { get; set; }

        [MaxLength(50)]
        public string? DocumentGradientDirection { get; set; }

        public bool? DocumentLogoUploaded { get; set; }

        [MaxLength(255)]
        public string? DocumentLogoFileName { get; set; }

        public bool? DocumentShowBankDetails { get; set; }

        [MaxLength(255)]
        public string? DocumentCompanyName { get; set; }

        [MaxLength(500)]
        public string? DocumentCompanyAddress { get; set; }

        [MaxLength(255)]
        public string? DocumentCompanyPhone { get; set; }

        [MaxLength(255)]
        public string? DocumentCompanyEmail { get; set; }

        [MaxLength(255)]
        public string? DocumentTaxId { get; set; }

        [MaxLength(50)]
        public string? DocumentQuoteNumberPrefix { get; set; }

        [MaxLength(50)]
        public string? DocumentInvoiceNumberPrefix { get; set; }

        [MaxLength(100)]
        public string? DocumentPaymentTerms { get; set; }

        public string? DocumentFooterNote { get; set; }

        [MaxLength(255)]
        public string? DocumentBankName { get; set; }

        [MaxLength(255)]
        public string? DocumentBankAccount { get; set; }

        [MaxLength(255)]
        public string? DocumentBankRouting { get; set; }

        [MaxLength(20)]
        public string? MeasurementSystem { get; set; }

        [MaxLength(20)]
        public string? TemperatureUnit { get; set; }

        [MaxLength(20)]
        public string? AreaUnit { get; set; }

        [MaxLength(20)]
        public string? VolumeUnit { get; set; }
    }
}
