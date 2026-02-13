namespace ProbuildBackend.Models.DTO
{
    public class ExternalCompanyDto
    {
        public int Id { get; set; }
        public string Source { get; set; } = "Apollo";
        public string? ExternalId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Domain { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? LinkedinUrl { get; set; }
        public string? Phone { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }
        public string? Description { get; set; }
        public string? Industry { get; set; }
        public int? EmployeeCount { get; set; }
        public int? FoundedYear { get; set; }
    }
}
