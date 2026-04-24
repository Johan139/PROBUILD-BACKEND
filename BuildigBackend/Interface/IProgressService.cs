using BuildigBackend.Models.State;

namespace BuildigBackend.Interface
{
    public interface IProgressService
    {
        Task UpdateProgressAsync(int jobId, int currentStep, int totalSteps, string message);
        Task CompleteJobAsync(int jobId, string? resultUrl = null);
        Task FailJobAsync(int jobId, string errorMessage);
        JobProgressState? GetJobProgress(int jobId);
        void SetConnectionId(int jobId, string connectionId);
    }
}

