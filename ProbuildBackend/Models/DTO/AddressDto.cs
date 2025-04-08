namespace ProbuildBackend.Models.DTO
{
    public class AddressDto
    {
        public string FormattedAddress { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string GooglePlaceId { get; set; }
    }
}
