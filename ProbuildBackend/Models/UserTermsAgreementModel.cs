namespace ProbuildBackend.Models
{
    public class UserTermsAgreementModel
    {
        public int id { get; set; }
        public string UserId { get; set; }
        public DateTime? DateAgreed { get; set; }
    }
}
