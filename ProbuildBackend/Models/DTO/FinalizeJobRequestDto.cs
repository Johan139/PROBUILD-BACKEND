namespace ProbuildBackend.Models.DTO
{
    public class FinalizeJobRequestDto
    {
        public string ProjectName { get; set; }
        public ClientDetailsDto Client { get; set; }
        public AddressDto Address { get; set; }
    }

    public class ClientDetailsDto
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string CompanyName { get; set; }
        public string Position { get; set; }
    }

    public class AddressDto
    {
        public string FormattedAddress { get; set; }
        public string StreetNumber { get; set; }
        public string StreetName { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string PostalCode { get; set; }
        public string Country { get; set; }
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public string GooglePlaceId { get; set; }
    }
}
