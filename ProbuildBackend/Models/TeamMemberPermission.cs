namespace ProbuildBackend.Models
{
    public class TeamMemberPermission
    {
        public string TeamMemberId { get; set; }
        public TeamMember TeamMember { get; set; }

        public int PermissionId { get; set; }
        public Permission Permission { get; set; }
    }
}
