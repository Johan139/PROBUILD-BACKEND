namespace ProbuildBackend.Models.DTO
{
    public class JobDto
    {

        public int JobId { get; set; }
        public string? ProjectName { get; set; }
        public string? JobType { get; set; }
        public int Qty { get; set; }
        public DateTime DesiredStartDate { get; set; }
        public string? WallStructure { get; set; }
        public string? WallStructureSubtask { get; set; }
        public string? WallInsulation { get; set; }
        public string? WallInsulationSubtask { get; set; }
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
        public string? Address { get; set; }
        public int Stories { get; set; }
        public double BuildingSize { get; set; }
        public string? Status { get; set; }
        public string? BiddingType { get; set; }
        public string? OperatingArea { get; set; }
        public string? UserId { get; set; }
        public List<IFormFile>? Blueprint { get; set; }
        public string? SessionId { get; set; } // Add sessionId to link documents
        public List<string>? TemporaryFileUrls { get; set; } // Add to pass the list of uploaded file URLs
        public string StreetNumber { get; set; }
        public string StreetName { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string PostalCode { get; set; }
        public string Country { get; set; }
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }
        public string GooglePlaceId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string? Phone { get; set; }
        public string? CompanyName { get; set; }
        public string? Position { get; set; }
        public int? Progress { get; set; }
        public string AnalysisType { get; set; }
        public List<string> PromptKeys { get; set; }
        public string? UserContextText { get; set; }
        public IFormFile? UserContextFile { get; set; }
        public bool GenerateDetailsWithAi { get; set; }
        public List<string>? Trades { get; set; }
        public DateTime PotentialStartDate { get; set; }
        public DateTime PotentialEndDate { get; set; }
        public int DurationInDays { get; set; }
        public List<JobSubtasksModel>? Subtasks { get; set; }
        public int NumberOfBids { get; set; }
        public string? ClientName { get; set; }
        public string? ClientCompanyName { get; set; }
        public double ClientRating { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? BiddingStartDate { get; set; }
        public string? BudgetLevel { get; set; }
    }
}
