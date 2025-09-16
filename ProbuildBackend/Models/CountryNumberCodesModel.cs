namespace ProbuildBackend.Models
{
    public class CountryNumberCodesModel
    {
        public Guid Id { get; set; }
        public string CountryCode { get; set; }
        public DateTime CountryPhoneNumberCode { get; set; }
        public string CountryId { get; set; }
        public DateTime DateCreated { get; set; }
        public string CreatedBy { get; set; }

        public DateTime UpdatedDate { get; set; }
        public string UpdatedBy { get; set; }
    }
}
