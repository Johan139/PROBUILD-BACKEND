namespace ProbuildBackend.Models
{
  public class WalkthroughStep
  {
    public int Id { get; set; }
    public Guid SessionId { get; set; }
    public int StepIndex { get; set; }
    public string PromptKey { get; set; }
    public string AiResponse { get; set; }
    public string UserEditedResponse { get; set; }
    public string UserComments { get; set; }
    public bool IsComplete { get; set; }
    public DateTime Timestamp { get; set; }
    public string ConversationId { get; set; }
    public virtual WalkthroughSession Session { get; set; }
  }
}