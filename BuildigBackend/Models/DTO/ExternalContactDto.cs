namespace BuildigBackend.Models.DTO
{
    public class ExternalContactDto
    {
        public int Id { get; set; }
        public string Source { get; set; } = "Apollo";
        public string? ExternalId { get; set; }
        public int ExternalCompanyId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? FullName { get; set; }
        public string? Title { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? LinkedinUrl { get; set; }
    }
}

