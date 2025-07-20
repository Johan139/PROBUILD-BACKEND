using System.Text.Json.Serialization;

namespace ProbuildBackend.Models.DTO
{
    public class UpdateJobAddressDto
    {
        [JsonPropertyName("street_number")]
        public string? StreetNumber { get; set; }
        [JsonPropertyName("street_name")]
        public string? StreetName { get; set; }
        [JsonPropertyName("city")]
        public string? City { get; set; }
        [JsonPropertyName("state")]
        public string? State { get; set; }
        [JsonPropertyName("postal_code")]
        public string? PostalCode { get; set; }
        [JsonPropertyName("country")]
        public string? Country { get; set; }
        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }
        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }
        [JsonPropertyName("formatted_address")]
        public string? FormattedAddress { get; set; }
        [JsonPropertyName("google_place_id")]
        public string? GooglePlaceId { get; set; }
    }
}
