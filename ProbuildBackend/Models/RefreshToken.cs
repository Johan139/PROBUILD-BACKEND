using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class RefreshToken
    {
        [Key]
        public int Id { get; set; }
        public string UserId { get; set; }
        public string Token { get; set; }
        public DateTime Expires { get; set; }
        public DateTime Created { get; set; }
        public DateTime? Revoked { get; set; }

        [ForeignKey("UserId")]
        public UserModel UserModel { get; set; }
    }
}
