using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class Portfolio
    {
        [Key]
        public int Id { get; set; }
        public string UserId { get; set; }
        public UserModel User { get; set; }
        public ICollection<JobModel> Jobs { get; set; }
    }
}
