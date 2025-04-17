namespace ProbuildBackend.Models.DTO
{
    public class JobAssignmentDto
    {
        public int Id { get; set; }
        public string? ProjectName { get; set; }
        public string? JobType { get; set; }
        public string? Address { get; set; }
        public int Stories { get; set; }
        public double BuildingSize { get; set; }
        public string? Status { get; set; }
        public List<JobUser> JobUser { get; set; }
    }

    public class JobUser
    {
        public string Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? UserType { get; set; }
        public string? JobRole { get; set; }
    }

    public class JobAssignmentList
    {
        public List<JobAssignmentDto> JobAssignment;
    }

    public class JobAssignment
    {
        public string UserId { get; set; }
        public int JobId { get; set; }
        public string? JobRole { get; set; }
    }
}
