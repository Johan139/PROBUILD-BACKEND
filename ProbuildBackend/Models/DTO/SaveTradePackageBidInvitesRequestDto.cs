using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models.DTO
{
    public class SaveTradePackageBidInvitesRequestDto
    {
        [Required]
        public int JobId { get; set; }

        [Required]
        public int TradePackageId { get; set; }

        [Required]
        public List<SaveTradePackageBidInviteRowDto> Invitees { get; set; } = new();
    }

    public class SaveTradePackageBidInviteRowDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string? ContactName { get; set; }

        public string? CompanyName { get; set; }

        public int? ExternalCompanyId { get; set; }

        public int? ExternalContactId { get; set; }
    }
}
