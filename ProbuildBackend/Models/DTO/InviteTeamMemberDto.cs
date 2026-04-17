using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class InviteTeamMemberDto
    {
        [Required]
        public string FirstName { get; set; }

        [Required]
        public string LastName { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        public string? PhoneNumber { get; set; }

        [Required]
        public string Role { get; set; }

        public string? HourlyRate { get; set; }

        public string? YearsExperience { get; set; }

        public string? Certifications { get; set; }

        public List<string>? Specialties { get; set; }

        public List<TeamMemberCertificationFileDto>? CertificationFiles { get; set; }
    }

    public class UpdateTeamMemberDto
    {
        [Required]
        public string Role { get; set; }

        public string? HourlyRate { get; set; }

        public string? YearsExperience { get; set; }

        public string? Certifications { get; set; }

        public List<string>? Specialties { get; set; }

        public List<TeamMemberCertificationFileDto>? CertificationFiles { get; set; }
    }
}
