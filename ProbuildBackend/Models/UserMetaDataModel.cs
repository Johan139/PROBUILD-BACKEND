using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class UserMetaDataModel
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public string UserId { get; set; }
        public string? IpAddress { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? Country { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? TimeZone { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string OperatingSystem { get; set; }
    }
}
