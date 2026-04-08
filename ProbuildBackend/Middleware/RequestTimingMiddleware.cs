using System.Diagnostics;

namespace ProbuildBackend.Middleware
{
    public class RequestTimingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestTimingMiddleware> _logger;
        private readonly long _slowRequestThresholdMs;

        public RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
        {
            _next = next;
            _logger = logger;

            var thresholdString = Environment.GetEnvironmentVariable("SLOW_REQUEST_MS");
            _slowRequestThresholdMs = long.TryParse(thresholdString, out var parsed) && parsed > 0
                ? parsed
                : 500;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await _next(context);
            }
            finally
            {
                sw.Stop();

                if (sw.ElapsedMilliseconds >= _slowRequestThresholdMs)
                {
                    _logger.LogWarning(
                        "Slow request {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                        context.Request.Method,
                        context.Request.Path.Value,
                        context.Response.StatusCode,
                        sw.ElapsedMilliseconds
                    );
                }
            }
        }
    }
}
