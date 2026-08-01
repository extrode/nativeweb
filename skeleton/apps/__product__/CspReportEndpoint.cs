using System.Text;

namespace __PRODUCT__;

// POST /csp-report: sink for browser CSP violation reports. Both delivery
// mechanisms land here -- the legacy report-uri POST (application/csp-report)
// and the Reporting API's report-to batches (application/reports+json), per the
// two directives SecurityHeadersMiddleware emits. The body is attacker-shaped
// JSON from an unauthenticated endpoint, so it is never deserialized (which also
// keeps reflection-based JSON out of the NativeAOT build): read a bounded slice,
// log it raw for the operator to inspect, return 204. Rate-limited per IP so the
// sink cannot be used as a log flooder. Skeleton copy -- the product owns this
// file after stamping.
internal static class CspReportEndpoint
{
    // Real violation reports are well under 2 KB; 8 KB leaves headroom for long
    // blocked-uri/source-file URLs and report-to batching without letting a
    // hostile client push megabytes into the log. Anything longer is truncated,
    // not rejected -- a partial report still names the violated directive.
    private const int MaxReportBytes = 8 * 1024;

    private static readonly Action<ILogger, string, Exception?> LogReport =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "CspViolationReport"),
            "CSP violation report: {Report}");

    public static void MapCspReportEndpoint(this WebApplication app)
    {
        app.MapPost("/csp-report", async (HttpContext context) =>
        {
            byte[] buffer = new byte[MaxReportBytes];
            int total = 0;
            while (total < MaxReportBytes)
            {
                int read = await context.Request.Body.ReadAsync(
                    buffer.AsMemory(total, MaxReportBytes - total), context.RequestAborted);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            if (total > 0)
            {
                ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("__PRODUCT__.CspReport");
                LogReport(logger, Sanitize(Encoding.UTF8.GetString(buffer, 0, total)), null);
            }

            return Results.NoContent();
        }).RequireRateLimiting("csp-report");
    }

    // The body is unauthenticated attacker input headed for the log: CR/LF would
    // forge log entries and C0/C1 controls can carry ANSI escapes into an
    // operator's terminal. Real reports are JSON, which never needs raw control
    // characters (they arrive \u-escaped), so flattening them to spaces loses
    // nothing legitimate.
    internal static string Sanitize(string report)
    {
        // DEL (0x7F) through the C1 range (0x9F), written as hex casts because
        // the escape form of these characters cannot survive a plain-text copy.
        char[]? sanitized = null;
        for (int i = 0; i < report.Length; i++)
        {
            char c = report[i];
            if (c is < ' ' or (>= (char)0x7F and <= (char)0x9F))
            {
                sanitized ??= report.ToCharArray();
                sanitized[i] = ' ';
            }
        }

        return sanitized is null ? report : new string(sanitized);
    }
}
