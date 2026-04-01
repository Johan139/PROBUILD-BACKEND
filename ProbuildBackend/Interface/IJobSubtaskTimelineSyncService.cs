namespace ProbuildBackend.Interface
{
    /// <summary>
    /// Parses the Phase N: Timeline markdown table from analysis reports and persists rows to JobSubtasks.
    /// </summary>
    public interface IJobSubtaskTimelineSyncService
    {
        /// <summary>
        /// When the report contains at least one timeline row, removes existing subtasks for the job and inserts parsed rows.
        /// If no timeline table is found, leaves existing subtasks unchanged.
        /// </summary>
        Task ReplaceSubtasksFromReportAsync(
            int jobId,
            string fullResponse,
            CancellationToken cancellationToken = default
        );
    }
}
