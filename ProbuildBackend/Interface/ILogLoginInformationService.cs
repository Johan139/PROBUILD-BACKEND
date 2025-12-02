namespace ProbuildBackend.Interface
{
    public interface ILogLoginInformationService
    {
        Task LogLoginAsync(
            Guid userId,
            string ip,
            string userAgent,
            bool success,
            string metadata = null,
            int keep = 5
        );
    }
}
