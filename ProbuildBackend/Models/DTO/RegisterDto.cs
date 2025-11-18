using System.Text.Json.Serialization;

namespace ProbuildBackend.Models.DTO
{
    public class RegisterDto
    {
        public string? Id { get; set; }
        public string? UserName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
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
        public string? JobPreferences { get; set; }
        public string? DeliveryArea { get; set; }
        public string? DeliveryTime { get; set; }
        public string? Country { get; set; }
        public string? State { get; set; }
        public string? City { get; set; }
        public string? SubscriptionPackage { get; set; }
        public string? Password { get; set; }

        public string StreetNumber { get; set; }
        public string StreetName { get; set; }
        public string PostalCode { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string FormattedAddress { get; set; }
        public string GooglePlaceId { get; set; }

        public string CountryNumberCode { get; set; }
        public string CountryCode { get; set; }

        public string? AddressType { get; set; }

        public string? SessionId { get; set; } // Add sessionId to link documents

        [JsonPropertyName("ipAddress")]
        public string? IpAddress { get; set; }
        [JsonPropertyName("countryFromIP")]
        public string? CountryFromIP { get; set; }
        [JsonPropertyName("regionFromIP")]
        public string? RegionFromIP { get; set; } // changed
        [JsonPropertyName("cityFromIP")]
        public string? CityFromIP { get; set; }
        [JsonPropertyName("latitudeFromIP")]
        public decimal? LatitudeFromIP { get; set; }
        [JsonPropertyName("longitudeFromIP")]
        public decimal? LongitudeFromIP { get; set; }

        [JsonPropertyName("operatingSystem")]
        public string? OperatingSystem { get; set; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }
    }
}
