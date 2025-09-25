namespace ProbuildBackend.Models
{
    public class ProjectModel
    {
        public int Id { get; set; }
        public string? ProjectName { get; set; }

        public string? ForemanId { get; set; }
        public UserModel? Foreman { get; set; }

        public string? ContractorId { get; set; }
        public UserModel? Contractor { get; set; }

        public string? JobType { get; set; }
        public int Qty { get; set; }
        public DateTime DesiredStartDate { get; set; }
        public string? WallStructure { get; set; }
        public string? WallStructureSubtask { get; set; }
        public string? WallStructureStatus { get; set; }

        public string? SubContractorWallStructureId { get; set; }
        public UserModel? SubContractorWallStructure { get; set; }

        public string? WallInsulation { get; set; }
        public string? WallInsulationSubtask { get; set; }
        public string? WallInsulationStatus { get; set; }

        public string? SubContractorWallInsulationId { get; set; }
        public UserModel? SubContractorWallInsulation { get; set; }

        public string? RoofStructure { get; set; }
        public string? RoofStructureSubtask { get; set; }
        public string? RoofStructureStatus { get; set; }


        public string? SubContractorRoofStructureId { get; set; }
        public UserModel? SubContractorRoofStructure { get; set; }

        public string? RoofType { get; set; }
        public string? RoofTypeSubtask { get; set; }
        public string? RoofTypeStatus { get; set; }


        public string? SubContractorRoofTypeId { get; set; }
        public UserModel? SubContractorRoofType { get; set; }


        public string? RoofInsulation { get; set; }
        public string? RoofInsulationSubtask { get; set; }
        public string? RoofInsulationStatus { get; set; }


        public string? SubContractorRoofInsulationId { get; set; }
        public UserModel? SubContractorRoofInsulation { get; set; }

        public string? Foundation { get; set; }
        public string? FoundationSubtask { get; set; }
        public string? FoundationStatus { get; set; }


        public string? SubContractorFoundationId { get; set; }
        public UserModel? SubContractorFoundation { get; set; }

        public string? Finishes { get; set; }
        public string? FinishesSubtask { get; set; }
        public string? FinishesStatus { get; set; }



        public string? SubContractorFinishesId { get; set; }
        public UserModel? SubContractorFinishes { get; set; }

        public string? ElectricalSupplyNeeds { get; set; }
        public string? ElectricalSupplyNeedsSubtask { get; set; }
        public string? ElectricalStatus { get; set; }


        public string? SubContractorElectricalSupplyNeedsId { get; set; }
        public UserModel? SubContractorElectricalSupplyNeeds { get; set; }

        public int Stories { get; set; }
        public double BuildingSize { get; set; }
        public string? BlueprintPath { get; set; }
        public string? OperatingArea { get; set; }

        public string? UserId { get; set; }
        public string? Status { get; set; }

        // public ICollection<NotificationModel>? Notifications { get; set; }
    }
}
