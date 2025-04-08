﻿using System.ComponentModel.DataAnnotations;

namespace ProbuildBackend.Models
{
    public class JobModel
    {
        public int Id { get; set; }
        [Required] public string? ProjectName { get; set; }
        [Required] public string? JobType { get; set; }
        [Required] public int Qty { get; set; }
        [Required] public DateTime DesiredStartDate { get; set; }
        [Required] public string? WallStructure { get; set; }
        public string? WallStructureSubtask { get; set; }
        [Required] public string? WallInsulation { get; set; }
        public string? WallInsulationSubtask { get; set; }
        [Required] public string? RoofStructure { get; set; }
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
        public string? OperatingArea { get; set; }
        public string? Address { get; set; }
        [Required] public string? UserId { get; set; }
        public UserModel? User { get; set; }
        public ICollection<BidModel>? Bids { get; set; }
        public string? Blueprint { get; set; }

        public ICollection<JobDocumentModel>? Documents { get; set; } // Add the list of associated documents

        //public int? AddressId { get; set; }
    }
}