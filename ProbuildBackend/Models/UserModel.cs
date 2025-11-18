using Microsoft.AspNetCore.Identity;

namespace ProbuildBackend.Models
{
    public class UserModel : IdentityUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? UserType { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyRegNo { get; set; }
        public string? VatNo { get; set; }
        public string? ConstructionType { get; set; }
        public string? NrEmployees { get; set; }
        public string? YearsOfOperation { get; set; }
        public string? CertificationStatus { get; set; }
        public string? CertificationDocumentPath { get; set; }
        public string? Availability { get; set; }
        public string? Trade { get; set; }
        public string? SupplierType { get; set; }
        public string? ProductsOffered { get; set; }
        public int NotificationRadiusMiles { get; set; }
        public string? JobPreferences { get; set; }
        public string? DeliveryArea { get; set; }
        public string? DeliveryTime { get; set; }
        public string? Country { get; set; }
        public string? State { get; set; }
        public string? City { get; set; }
        public string? SubscriptionPackage { get; set; }

        public string? CountryNumberCode { get; set; }
        public string? StripeCustomerId { get; set; }
        public bool IsVerified { get; set; }
        public DateTime? DateCreated { get; set; }
        public bool IsTimedOut { get; set; }
        public bool IsActive { get; set; } = true;
        public float? ProbuildRating { get; set; }
        public float? GoogleRating { get; set; }
        public bool NotificationEnabled { get; set; } = true;
        public ICollection<BidModel>? Bids { get; set; }
        public ICollection<NotificationModel>? Notifications { get; set; }
        public int QuoteCount { get; set; } = 0;
        public DateTime LastQuoteReset { get; set; } = DateTime.UtcNow;
        public int QuoteRefreshRound { get; set; } = 1;
        public Portfolio? Portfolio { get; set; }
        public ICollection<Invitation>? SentInvitations { get; set; }

        public ICollection<UserAddressModel>? UserAddresses { get; set; }
    }
}