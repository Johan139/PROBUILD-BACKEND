namespace ProbuildBackend.Models
{
    public class StatesModel
    {
        public Guid Id { get; set; }
        public string? StateCode { get; set; }
        public string? StateName { get; set; }

        public string? CountryId { get; set; }
        public DateTime? DateCreated { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public string? UpdatedBy { get; set; }
    }
}
