namespace ProbuildBackend.Models.DTO
{
    public class UpdateConversationTitleDto
    {
        public string ConversationId { get; set; }
        public string NewTitle { get; set; }
    }

    public class StartConversationDto
    {
        public string InitialMessage { get; set; }
        public List<string> PromptKeys { get; set; }
        public List<string> BlueprintUrls { get; set; }
        public string UserType { get; set; }
    }

    public class PostMessageDto
    {
        public string Message { get; set; }
        public IFormFileCollection? Files { get; set; }
        public List<string>? PromptKeys { get; set; }
    }
}
