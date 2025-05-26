namespace ProbuildBackend.Models
{
    public class JobsTermsAgreement
    {
        public int id { get; set; }
        public string UserId { get; set; }
        public DateTime? DateAgreed { get; set; }
        public int JobId { get; set; }
    }
}
