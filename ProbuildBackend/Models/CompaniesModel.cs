using ProbuildBackend.Models.DTO;
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

        public string? ProductsOffered { get; set; }

        public string? JobPreferences { get; set; }

        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? CountryNumberCode { get; set; }

        public string? DeliveryArea { get; set; }

        [MaxLength(255)]
        public string? DeliveryTime { get; set; }

    }
}
