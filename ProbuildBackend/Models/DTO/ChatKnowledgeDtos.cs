namespace ProbuildBackend.Models.DTO
{
    public class ChatKnowledgeContextRequest
    {
        public string? UserType { get; set; }
        public string? UserMessage { get; set; }
        public string? HelpIntent { get; set; }
        public string? CurrentRoute { get; set; }
        public string? CurrentFeature { get; set; }
        public string? CurrentStage { get; set; }
        public string? ProjectName { get; set; }
        public List<string> PromptKeys { get; set; } = new();
    }

    public class ChatKnowledgeResolutionResult
    {
        public string BasePrompt { get; set; } = string.Empty;
        public List<string> SelectedKnowledgeFiles { get; set; } = new();
        public string ComposedSystemPrompt { get; set; } = string.Empty;
    }
}
