namespace ProbuildBackend.Models.DTO
{
  public class DashboardProjectDto
  {
    public int JobId { get; set; }
    public string ProjectName { get; set; }
    public string Address { get; set; }
    public string Status { get; set; }
    public string ThumbnailUrl { get; set; }
    public int Progress { get; set; }
    public int Team { get; set; }
    public string ClientName { get; set; }
    public DateTime CreatedAt { get; set; }
  }
}