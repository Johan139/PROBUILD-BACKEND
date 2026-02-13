namespace ProbuildBackend.Options
{
    public class ApolloOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://api.apollo.io/api/v1";
        public int DefaultSearchLimit { get; set; } = 12;
        public int RequestTimeoutSeconds { get; set; } = 20;
    }
}
