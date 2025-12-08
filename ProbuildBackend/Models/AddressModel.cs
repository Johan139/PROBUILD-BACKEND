using NetTopologySuite.Geometries;

namespace ProbuildBackend.Models
{
    public class AddressModel
    {
        public long Id { get; set; }
        public string? StreetNumber { get; set; }
        public string? StreetName { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public Point Location { get; set; }
        public string? FormattedAddress { get; set; }
        public string? GooglePlaceId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? JobId { get; set; }
    }
}
