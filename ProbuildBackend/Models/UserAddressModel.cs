using Elastic.Apm.Api;
using NetTopologySuite.Geometries;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ProbuildBackend.Models
{
    public class UserAddressModel
    {
        public long Id { get; set; }
        public string StreetNumber { get; set; }
        public string StreetName { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string PostalCode { get; set; }
        public string Country { get; set; }

        public bool Deleted { get; set; }
        public string CountryCode { get; set; }

        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        [JsonIgnore]
        public Point Location { get; set; }
        public string FormattedAddress { get; set; }
        public string GooglePlaceId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string? AddressType { get; set; }

        [ForeignKey(nameof(User))]
        public string? UserId { get; set; }

        [JsonIgnore]
        public UserModel? User { get; set; }
    }
}
