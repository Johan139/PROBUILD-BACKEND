using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ProbuildBackend.Models
{
    public class JobTradeBudget
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int JobId { get; set; }

        [ForeignKey("JobId")]
        [JsonIgnore]
        public JobModel? Job { get; set; }

        [Required]
        public string? TradeName { get; set; }

        public decimal Budget { get; set; }
    }
}