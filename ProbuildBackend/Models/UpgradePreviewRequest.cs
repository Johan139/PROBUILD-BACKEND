namespace ProbuildBackend.Models
{
    public class UpgradePreviewRequest
    {
        public string SubscriptionId { get; set; } = default!;
        public string PackageName { get; set; } = default!; // what your <mat-option [value]="s.value"> sends
        public long? ProrationDateUnix { get; set; } // optional; if omitted, use "now"
        public string? UserId { get; set; } // optional passthrough if you need it
    }
}
