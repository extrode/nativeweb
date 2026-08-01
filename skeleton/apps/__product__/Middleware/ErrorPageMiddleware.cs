using System.Text.Encodings.Web;

namespace __PRODUCT__.Middleware;

// Central HTML error rendering: an unhandled exception, or any response that
// reaches >= 400 without writing a body of its own (e.g. routing's unmatched-
// endpoint 404), gets a friendly HTML page -- or, for an htmx request, a bare
// fragment sized to swap into whatever target issued the request. Endpoints
// that already write their own body are untouched: the HasStarted check means
// this only ever fills in a response nobody wrote. Full pages come from the
// static shell (spec 003 R39), never the renderer: the error path must keep
// working when the render layer is the thing broken. Skeleton copy -- the
// product owns this file after stamping.
internal sealed partial class ErrorPageMiddleware(RequestDelegate next, StaticErrorShell shell, ILogger<ErrorPageMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);

            if (!context.Response.HasStarted && context.Response.StatusCode >= 400)
            {
                await WriteErrorAsync(context, context.Response.StatusCode).ConfigureAwait(false);
            }
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            // Kestrel's own verdict on a malformed or over-limit request (413
            // when the body exceeds MaxRequestBodySize, 400 for protocol
            // violations). Surface its status rather than mislabeling a client
            // fault as a 500 -- and don't log it as an unhandled exception,
            // because a flood of oversized requests is the client's doing.
            context.Response.Clear();
            context.Response.StatusCode = ex.StatusCode;
            await WriteErrorAsync(context, ex.StatusCode).ConfigureAwait(false);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            LogUnhandledException(logger, ex, context.Request.Method, context.Request.Path);
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError).ConfigureAwait(false);
        }
    }

    private async Task WriteErrorAsync(HttpContext context, int statusCode)
    {
        (string? title, string? message) = Describe(statusCode);

        context.Response.ContentType = "text/html; charset=utf-8";
        // Unconditional, not just on the full-page branch: 404 is heuristically
        // cacheable per RFC 9110 and a cached 404 for a URL that later exists
        // would be a stale lie -- and when the request came in through a static
        // clean URL the marker below (or one already set by the rewrite) makes
        // SecurityHeadersMiddleware skip its own no-store, so an htmx fragment
        // error on a marked request would otherwise carry no cache directive
        // at all.
        context.Response.Headers.CacheControl = "no-store";
        string html;
        if (context.Request.Headers.ContainsKey("HX-Request"))
        {
            html = $"<h1>{HtmlEncoder.Default.Encode(title)}</h1><p>{HtmlEncoder.Default.Encode(message)}</p>";
        }
        else
        {
            // The marker gives the full page the nonce-free static CSP -- it
            // renders from the static shell and carries no scripts.
            context.Items[StaticPagesMiddleware.ItemKey] = true;
            html = shell.Render(title, message);
        }

        await context.Response.WriteAsync(html).ConfigureAwait(false);
    }

    internal static (string Title, string Message) Describe(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ("Bad request", "The request could not be understood. Check it and try again."),
        StatusCodes.Status404NotFound => ("Not found", "The page you're looking for doesn't exist."),
        StatusCodes.Status405MethodNotAllowed => ("Not allowed", "That action isn't supported here."),
        StatusCodes.Status401Unauthorized => ("Sign in required", "You need to sign in to view this page."),
        StatusCodes.Status403Forbidden => ("Forbidden", "You don't have permission to view this page."),
        StatusCodes.Status429TooManyRequests => ("Too many requests", "Slow down and try again in a moment."),
        StatusCodes.Status413PayloadTooLarge => ("Request too large", "What you sent exceeds the size limit. Trim it down and try again."),
        _ => ("Something went wrong", "An unexpected error occurred. Please try again."),
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception handling {Method} {Path}")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, string method, string path);
}
