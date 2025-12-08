namespace ProbuildBackend.Models.DTO
{
    public class UserLoginAuditDTO
    {
        public Guid UserId { get; set; } // or int
        public DateTime LoginTime { get; set; }
        public string IpAddress { get; set; }
        public string UserAgent { get; set; }
        public bool IsSuccess { get; set; }
        public string Metadata { get; set; }
    }
}
