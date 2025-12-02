namespace ProbuildBackend.Models
{
    public class CountriesModel
    {
        public Guid Id { get; set; }
        public string? CountryName { get; set; }
        public string? CountryCode { get; set; }
        public DateTime? DateCreated { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string? UpdatedBy { get; set; }
    }
}
