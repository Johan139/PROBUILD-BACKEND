// Ensure Bit is a class, not a struct
namespace ProbuildBackend.Models
{
    public class BidModel
    {
        public int Id { get; set; }
        public string? Task { get; set; }

        public int Duration { get; set; }

        public int JobId { get; set; }
        public JobModel? Job { get; set; }

        public string? UserId { get; set; }
        public UserModel? User { get; set; }
        public byte[]? Quote { get; set; }
    }
}