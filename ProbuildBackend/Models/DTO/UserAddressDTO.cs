using System.Text.Json.Serialization;

namespace ProbuildBackend.Models.DTO
{
    public class UserAddressDTO
    {

        [JsonPropertyName("streetNumber")]
        public string StreetNumber { get; set; }

        [JsonPropertyName("streetName")]
        public string StreetName { get; set; }

        [JsonPropertyName("city")]
        public string City { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; }

        [JsonPropertyName("postalCode")]
        public string PostalCode { get; set; }

        [JsonPropertyName("country")]
        public string Country { get; set; }

        [JsonPropertyName("countryCode")]
        public string CountryCode { get; set; }

        [JsonPropertyName("latitude")]
        public decimal? Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public decimal? Longitude { get; set; }

        //[JsonIgnore]
        //public Point Location { get; set; }
        [JsonPropertyName("formattedAddress")]
        public string FormattedAddress { get; set; }
        [JsonPropertyName("googlePlaceId")]
        public string GooglePlaceId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string AddressType { get; set; }
        // [ForeignKey(nameof(User))]
        [JsonPropertyName("userId")]
        public string? UserId { get; set; }

        //[JsonIgnore]
        //public UserModel? User { get; set; }
    }
}