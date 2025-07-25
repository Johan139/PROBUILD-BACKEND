using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class Permission
    {
        [Key]
        public int PermissionId { get; set; }

        [Required]
        [StringLength(100)]
        public string PermissionName { get; set; }

        [StringLength(255)]
        public string Description { get; set; }

        public ICollection<TeamMemberPermission> TeamMemberPermissions { get; set; }
    }
}
