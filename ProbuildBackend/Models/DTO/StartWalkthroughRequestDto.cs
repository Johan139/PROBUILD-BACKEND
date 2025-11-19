namespace ProbuildBackend.Models.DTO
{
  public class StartWalkthroughRequestDto
  {
    public List<string> DocumentUrls { get; set; }
    public DateTime StartDate { get; set; }
    public string AnalysisType { get; set; }
    public List<string> PromptKeys { get; set; }
    public string BudgetLevel { get; set; }
  }
}