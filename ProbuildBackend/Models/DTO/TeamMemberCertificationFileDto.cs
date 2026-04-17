namespace ProbuildBackend.Models.DTO
{
    public class TeamMemberCertificationFileDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? UploadedAt { get; set; }
        public string? Url { get; set; }
    }
}
