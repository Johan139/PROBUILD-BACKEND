namespace ProbuildBackend.Models
{
    public class JobAssignmentModel
    {
        public string UserId { get; set; }
        public int JobId { get; set; }
        public string? JobRole { get; set; }

        public UserModel User { get; set; }
        public JobModel Job { get; set; }
    }
}
