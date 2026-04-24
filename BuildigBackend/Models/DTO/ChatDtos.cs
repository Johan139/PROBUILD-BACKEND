namespace BuildigBackend.Models.DTO
{
    public class UpdateConversationTitleDto
    {
        public string ConversationId { get; set; }
        public string NewTitle { get; set; }
    }

    public class StartConversationDto
    {
        public string InitialMessage { get; set; }
        public List<string>? PromptKeys { get; set; }
        public List<string>? BlueprintUrls { get; set; }
        public string UserType { get; set; }
        public string? HelpIntent { get; set; }
        public string? CurrentRoute { get; set; }
        public string? CurrentFeature { get; set; }
        public string? CurrentStage { get; set; }
        public string? ProjectName { get; set; }
    }

    public class PostMessageDto
    {
        public string Message { get; set; }
        public IFormFileCollection? Files { get; set; }
        public List<string>? PromptKeys { get; set; }
        public List<string>? DocumentUrls { get; set; }
        public string? HelpIntent { get; set; }
        public string? CurrentRoute { get; set; }
        public string? CurrentFeature { get; set; }
        public string? CurrentStage { get; set; }
        public string? ProjectName { get; set; }
    }
}

