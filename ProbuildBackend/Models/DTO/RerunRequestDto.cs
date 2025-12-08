namespace ProbuildBackend.Models.DTO
{
    public class RerunRequestDto
    {
        public string OriginalAiResponse { get; set; }
        public string UserEditedResponse { get; set; }
        public string UserComments { get; set; }
        public bool ApplyCostOptimisation { get; set; }
    }
}
