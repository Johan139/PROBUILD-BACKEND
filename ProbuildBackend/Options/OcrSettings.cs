namespace ProbuildBackend.Options
{
    public class OcrSettings
    {
        public int Dpi { get; set; } = 72;
        public int MaxImageWidth { get; set; } = 1024;
        public int MaxImageHeight { get; set; } = 1024;
        public int MaxConcurrentPages { get; set; } = 4;
        public int ThrottleDelayMs { get; set; } = 2000;
        public int MaxTokens { get; set; } = 4000;
    }
}
