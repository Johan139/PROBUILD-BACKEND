using Microsoft.AspNetCore.SignalR;
using ProbuildBackend.Interface;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models.State;
using System.Collections.Concurrent;

namespace ProbuildBackend.Services
{
    public class ProgressService : IProgressService
    {
        // Static so it persists across scoped instances
        private static readonly ConcurrentDictionary<int, JobProgressState> _progressCache = new();

        private readonly IHubContext<ProgressHub> _hubContext;
        private readonly ILogger<ProgressService> _logger;

        private const int DEFAULT_TOTAL_STEPS = 32;

        public ProgressService(
            IHubContext<ProgressHub> hubContext,
            ILogger<ProgressService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public void SetConnectionId(int jobId, string connectionId)
        {
            _progressCache.AddOrUpdate(
                jobId,
                new JobProgressState { JobId = jobId, ConnectionId = connectionId },
                (key, existing) =>
                {
                    existing.ConnectionId = connectionId;
                    return existing;
                });
        }

        public JobProgressState? GetJobProgress(int jobId)
        {
            _progressCache.TryGetValue(jobId, out var state);
            return state;
        }

        public async Task UpdateProgressAsync(int jobId, int currentStep, int totalSteps, string message)
        {
            int percent = totalSteps > 0
                ? (int)Math.Round((double)currentStep / totalSteps * 100)
                : 0;

            var state = _progressCache.AddOrUpdate(
                jobId,
                new JobProgressState
                {
                    JobId = jobId,
                    Percent = percent,
                    CurrentStep = currentStep,
                    TotalSteps = totalSteps,
                    Message = message,
                    Status = "PROCESSING",
                    LastUpdated = DateTime.UtcNow
                },
                (key, existing) =>
                {
                    existing.Percent = percent;
                    existing.CurrentStep = currentStep;
                    existing.TotalSteps = totalSteps;
                    existing.Message = message;
                    existing.LastUpdated = DateTime.UtcNow;
                    return existing;
                });

            // Send via SignalR
            await SendSignalRUpdateAsync(state);

            _logger.LogInformation(
                "Progress: Job {JobId} - Step {CurrentStep}/{TotalSteps} ({Percent}%)",
                jobId, currentStep, totalSteps, percent);
        }

        public async Task CompleteJobAsync(int jobId, string? resultUrl = null)
        {
            var state = _progressCache.AddOrUpdate(
                jobId,
                new JobProgressState
                {
                    JobId = jobId,
                    Status = "PROCESSED",
                    Percent = 100,
                    CurrentStep = DEFAULT_TOTAL_STEPS,
                    TotalSteps = DEFAULT_TOTAL_STEPS,
                    Message = "Analysis complete",
                    ResultUrl = resultUrl,
                    LastUpdated = DateTime.UtcNow
                },
                (key, existing) =>
                {
                    existing.Status = "PROCESSED";
                    existing.Percent = 100;
                    existing.CurrentStep = DEFAULT_TOTAL_STEPS;
                    existing.Message = "Analysis complete";
                    existing.ResultUrl = resultUrl;
                    existing.LastUpdated = DateTime.UtcNow;
                    return existing;
                });

            // Send completion via SignalR
            await SendSignalRUpdateAsync(state, isComplete: true);

            if (!string.IsNullOrEmpty(state.ConnectionId))
            {
                try
                {
                    await _hubContext.Clients.Client(state.ConnectionId)
                        .SendAsync("AnalysisComplete", new
                        {
                            jobId,
                            resultUrl = resultUrl ?? BuildDefaultResultUrl(jobId)
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send AnalysisComplete for Job {JobId}", jobId);
                }
            }

            // Clean up cache after a delay (optional - keeps memory tidy)
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMinutes(30));
                _progressCache.TryRemove(jobId, out _);
            });

            _logger.LogInformation("Job {JobId} completed", jobId);
        }

        public async Task FailJobAsync(int jobId, string errorMessage)
        {
            var state = _progressCache.AddOrUpdate(
                jobId,
                new JobProgressState
                {
                    JobId = jobId,
                    Status = "FAILED",
                    Message = "Analysis failed",
                    ErrorMessage = errorMessage,
                    LastUpdated = DateTime.UtcNow
                },
                (key, existing) =>
                {
                    existing.Status = "FAILED";
                    existing.Message = "Analysis failed";
                    existing.ErrorMessage = errorMessage;
                    existing.LastUpdated = DateTime.UtcNow;
                    return existing;
                });

            await SendSignalRUpdateAsync(state, hasFailed: true);

            if (!string.IsNullOrEmpty(state.ConnectionId))
            {
                try
                {
                    await _hubContext.Clients.Client(state.ConnectionId)
                        .SendAsync("AnalysisFailed", new
                        {
                            jobId,
                            error = errorMessage
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send AnalysisFailed for Job {JobId}", jobId);
                }
            }

            _logger.LogError("Job {JobId} failed: {Error}", jobId, errorMessage);
        }

        private async Task SendSignalRUpdateAsync(JobProgressState state, bool isComplete = false, bool hasFailed = false)
        {
            if (string.IsNullOrEmpty(state.ConnectionId)) return;

            var update = new AnalysisProgressUpdate
            {
                JobId = state.JobId,
                StatusMessage = state.Message ?? "",
                CurrentStep = state.CurrentStep,
                TotalSteps = state.TotalSteps,
                IsComplete = isComplete,
                HasFailed = hasFailed,
                ErrorMessage = state.ErrorMessage ?? ""
            };

            try
            {
                await _hubContext.Clients.Client(state.ConnectionId)
                    .SendAsync("ReceiveAnalysisProgress", update);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR send failed for Job {JobId}", state.JobId);
            }
        }

        private string BuildDefaultResultUrl(int jobId)
        {
            var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL")
                ?? "http://localhost:4200";
            return $"{frontendUrl}/view-quote?jobId={jobId}";
        }
    }
}