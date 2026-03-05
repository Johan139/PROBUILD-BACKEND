namespace ProbuildBackend.Models.DTO
{
    public class CrmUserDetailsDto
    {
        public string Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public bool EmailConfirmed { get; set; }
        public string? PhoneNumber { get; set; }
        public string? CountryNumberCode { get; set; }
        public string? UserType { get; set; }
        public bool IsAdmin { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyRegNo { get; set; }
        public string? VatNo { get; set; }
        public string? Trade { get; set; }
        public string? SupplierType { get; set; }
        public string? SubscriptionPackage { get; set; }
        public string? Country { get; set; }
        public string? State { get; set; }
        public string? City { get; set; }
        public bool IsActive { get; set; }
        public bool IsVerified { get; set; }
        public CrmUserSubscriptionSummaryDto? Subscription { get; set; }
    }
}
