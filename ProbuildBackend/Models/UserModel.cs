using Microsoft.AspNetCore.Identity;
using ProbuildBackend.Models;

namespace ProbuildBackend.Models {
public class UserModel : IdentityUser
    {
        public string? Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? UserType { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyRegNo { get; set; }
        public string? VatNo { get; set; }
        public string? Email { get; set; }
        public string? ConstructionType { get; set; }
        public string? NrEmployees { get; set; }
        public string? YearsOfOperation { get; set; }
        public string? CertificationStatus { get; set; }
        public string? CertificationDocumentPath { get; set; }
        public string? Availability { get; set; }
        public string? Trade { get; set; }
        public string? SupplierType { get; set; }
        public string? ProductsOffered { get; set; }
        public string? ProjectPreferences { get; set; }
        public string? DeliveryArea { get; set; }
        public string? DeliveryTime { get; set; }
        public string? Country { get; set; }
        public string? State { get; set; }
        public string? City { get; set; }
        public string? SubscriptionPackage { get; set; }
        public bool IsVerified { get; set; }

        public ICollection<BidModel>? Bids { get; set; }
        public ICollection<NotificationModel>? Notifications { get; set; }
    }
}