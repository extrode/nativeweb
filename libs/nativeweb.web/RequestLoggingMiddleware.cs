using System.Diagnostics;

namespace NativeWeb.Web;

// Structured, one-line-per-request logging (R23). Uses the [LoggerMessage]
// source generator (compile-time, reflection-free) instead of interpolated
// ILogger calls so it stays AOT/trim safe. The TraceIdentifier rides along as
// the correlation id -- it is what an error page or a downstream log line
// would carry. The probes are skipped to keep the log readable under
// load-balancer polling.
public sealed partial class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Equals("/healthz", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.Equals("/readyz", StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            LogRequest(
                logger,
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                context.TraceIdentifier);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Method} {Path} responded {StatusCode} in {ElapsedMs}ms [{TraceId}]")]
    private static partial void LogRequest(ILogger logger, string method, PathString path, int statusCode, long elapsedMs, string traceId);
}
