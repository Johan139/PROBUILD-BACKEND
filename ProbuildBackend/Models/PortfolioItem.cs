using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class PortfolioItem
    {
        [Key]
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string ImageUrl { get; set; }
        public string UserId { get; set; }
        public UserModel User { get; set; }
    }
}
