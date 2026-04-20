namespace BuildigBackend.Models.DTO
{
    public class PlanningDataDto
    {
        public List<ProcurementItemDto> ProcurementItems { get; set; } =
            new List<ProcurementItemDto>();
        public List<CriticalPathPhaseDto> CriticalPath { get; set; } =
            new List<CriticalPathPhaseDto>();
    }

    public class ProcurementItemDto
    {
        public string Item { get; set; }
        public string LeadTime { get; set; }
        public string Vendor { get; set; }
        public string Status { get; set; }
        public decimal EstimatedCost { get; set; }
    }

    public class CriticalPathPhaseDto
    {
        public string Phase { get; set; }
        public string Duration { get; set; }
        public string Materials { get; set; }
        public string Id { get; set; }
        public int StartDay { get; set; }
        public int EndDay { get; set; }
        public List<string> Trades { get; set; } = new List<string>();
        public List<string> Milestones { get; set; } = new List<string>();
        public string Dependencies { get; set; }
        public List<string> CriticalItems { get; set; } = new List<string>();
        public List<string> Inspections { get; set; } = new List<string>();
        public decimal LaborHours { get; set; }
        public decimal MaterialCost { get; set; }
        public decimal LaborCost { get; set; }
        public decimal Cost { get; set; }
    }
}

