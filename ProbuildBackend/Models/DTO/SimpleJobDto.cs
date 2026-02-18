using System.ComponentModel.DataAnnotations;
namespace ProbuildBackend.Models.DTO
{
    public class SimpleJobDto
    {
        [Required]
        public string FullName { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        public string PhoneNumber { get; set; }

        [Required]
        public string CityArea { get; set; }

        [Required]
        public string ProjectType { get; set; }

        [Required]
        public string ProfessionalType { get; set; }

        public List<string>? SelectedTrades { get; set; }

        [Required]
        public string JobDescription { get; set; }

        public bool HasBlueprint { get; set; }

        public IFormFile? BlueprintFile { get; set; }
        public string? SessionId { get; set; }
        public string? Address { get; set; }
        public string? StreetNumber { get; set; }
        public string? StreetName { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }
        public string? GooglePlaceId { get; set; }
    }
}