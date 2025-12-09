using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProbuildBackend.Models
{
    public class JobModel
    {
        public int Id { get; set; }

        [Required]
        public string? ProjectName { get; set; }

        [Required]
        public string? JobType { get; set; }

        [Required]
        public int Qty { get; set; }

        [Required]
        public DateTime DesiredStartDate { get; set; }

        [Required]
        public string? WallStructure { get; set; }
        public string? WallStructureSubtask { get; set; }

        [Required]
        public string? WallInsulation { get; set; }
        public string? WallInsulationSubtask { get; set; }

        [Required]
        public string? RoofStructure { get; set; }
        public string? RoofStructureSubtask { get; set; }
        public string? RoofTypeSubtask { get; set; }
        public string? RoofInsulation { get; set; }
        public string? RoofInsulationSubtask { get; set; }
        public string? Foundation { get; set; }
        public string? FoundationSubtask { get; set; }
        public string? Finishes { get; set; }
        public string? FinishesSubtask { get; set; }
        public string? ElectricalSupplyNeeds { get; set; }
        public string? ElectricalSupplyNeedsSubtask { get; set; }
        public int Stories { get; set; }
        public double BuildingSize { get; set; }
        public string? Status { get; set; }
        public string? BiddingType { get; set; }
        public List<string>? RequiredSubcontractorTypes { get; set; }
        public string? OperatingArea { get; set; }
        public string? Address { get; set; }

        [Required]
        public string? UserId { get; set; }
        public UserModel? User { get; set; }
        public ICollection<BidModel>? Bids { get; set; }
        public string? Blueprint { get; set; }
        public ICollection<JobDocumentModel>? Documents { get; set; } 
        public ICollection<NotificationModel>? Notifications { get; set; }
        public ICollection<JobTradeBudget>? TradeBudgets { get; set; }
        public long? JobAddressId { get; set; }

        [ForeignKey("JobAddressId")]
        public AddressModel? JobAddress { get; set; }
        public int? PortfolioId { get; set; }

        [ForeignKey("PortfolioId")]
        public Portfolio? Portfolio { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? BiddingStartDate { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? ConversationId { get; set; }

        [ForeignKey("ConversationId")]
        public Conversation? Conversation { get; set; }
        public DateTime? ArchivedAt { get; set; }
    }
}
