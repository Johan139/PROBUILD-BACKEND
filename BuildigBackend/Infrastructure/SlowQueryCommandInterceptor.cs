using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BuildigBackend.Infrastructure
{
    public class SlowQueryCommandInterceptor : DbCommandInterceptor
    {
        private readonly ILogger<SlowQueryCommandInterceptor> _logger;
        private readonly long _slowSqlThresholdMs;

        public SlowQueryCommandInterceptor(ILogger<SlowQueryCommandInterceptor> logger)
        {
            _logger = logger;

            var thresholdString = Environment.GetEnvironmentVariable("SLOW_SQL_MS");
            _slowSqlThresholdMs = long.TryParse(thresholdString, out var parsed) && parsed > 0
                ? parsed
                : 250;
        }

        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result)
        {
            LogIfSlow(command, eventData);
            return base.ReaderExecuted(command, eventData, result);
        }

        public override object? ScalarExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            object? result)
        {
            LogIfSlow(command, eventData);
            return base.ScalarExecuted(command, eventData, result);
        }

        public override int NonQueryExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result)
        {
            LogIfSlow(command, eventData);
            return base.NonQueryExecuted(command, eventData, result);
        }

        private void LogIfSlow(DbCommand command, CommandExecutedEventData eventData)
        {
            var elapsedMs = (long)eventData.Duration.TotalMilliseconds;
            if (elapsedMs < _slowSqlThresholdMs)
            {
                return;
            }

            var sql = command.CommandText ?? string.Empty;
            if (sql.Length > 1500)
            {
                sql = sql.Substring(0, 1500) + "…";
            }

            _logger.LogWarning(
                "Slow SQL ({ElapsedMs}ms) {CommandType} {Sql}",
                elapsedMs,
                command.CommandType,
                sql
            );
        }
    }
}

