namespace BuildigBackend.Models.State
{
    public class JobProgressState
    {
        public int JobId { get; set; }
        public string Status { get; set; } = "PROCESSING";
        public int Percent { get; set; }
        public int CurrentStep { get; set; }
        public int TotalSteps { get; set; } = 32;
        public string? Message { get; set; }
        public string? ConnectionId { get; set; }
        public string? ResultUrl { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}

