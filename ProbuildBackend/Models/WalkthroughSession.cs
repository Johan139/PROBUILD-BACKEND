namespace ProbuildBackend.Models
{
  public class WalkthroughSession
  {
    public Guid Id { get; set; }
    public string UserId { get; set; }
    public int? JobId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string AnalysisType { get; set; }
    public string PromptSequenceJson { get; set; }
    public virtual ICollection<WalkthroughStep> Steps { get; set; }
  }
}