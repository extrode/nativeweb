using System.Net;

using Microsoft.AspNetCore.Http;


using Xunit;

namespace NativeWeb.Web.Tests.Unit;

// Direct coverage of the error-rendering seams: the middleware is carried
// verbatim from the NativeWeb skeleton, so it arrives with the tests that
// notice if the product edits it into something weaker.
public class ErrorPageMiddlewareTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Bad request")]
    [InlineData(HttpStatusCode.NotFound, "Not found")]
    [InlineData(HttpStatusCode.MethodNotAllowed, "Not allowed")]
    [InlineData(HttpStatusCode.Unauthorized, "Sign in required")]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden")]
    [InlineData(HttpStatusCode.TooManyRequests, "Too many requests")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "Request too large")]
    public void Describe_KnownStatusCode_ReturnsExpectedTitle(HttpStatusCode statusCode, string expectedTitle)
    {
        (string? title, string _) = ErrorPageMiddleware.Describe((int)statusCode);

        Assert.Equal(expectedTitle, title);
    }

    [Fact]
    public void Describe_UnmappedStatusCode_FallsBackToGenericMessage()
    {
        (string? title, string? message) = ErrorPageMiddleware.Describe((int)HttpStatusCode.InternalServerError);

        Assert.Equal("Something went wrong", title);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    // Kestrel's own verdict on an over-limit or malformed request must surface
    // with its own status code, not be swallowed by the generic catch as a 500.
    // The htmx branch renders a bare fragment without touching the static
    // shell, so a stub shell suffices here.
    [Fact]
    public async Task Invoke_NextThrowsBadHttpRequest_SurfacesKestrelsStatusCode()
    {
        var middleware = new ErrorPageMiddleware(
            _ => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            shell: new StaticErrorShell("{{title}} {{message}}"),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<ErrorPageMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Headers["HX-Request"] = "true";
        using var body = new MemoryStream();
        context.Response.Body = body;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        string html = System.Text.Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("Request too large", html, StringComparison.Ordinal);
    }

    // The full-page branch: shell substitution plus the marker and no-store.
    [Fact]
    public async Task Invoke_NotFoundWithoutHtmx_RendersTheShellAndMarksStatic()
    {
        var middleware = new ErrorPageMiddleware(
            _ => Task.CompletedTask,
            shell: new StaticErrorShell("<h1>{{title}}</h1><p>{{message}}</p>"),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<ErrorPageMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        using var body = new MemoryStream();
        context.Response.Body = body;

        await middleware.InvokeAsync(context);

        string html = System.Text.Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("<h1>Not found</h1>", html, StringComparison.Ordinal);
        Assert.True(context.Items.ContainsKey(StaticPagesMiddleware.ItemKey));
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    // The unhandled-exception catch: Clear() then 500 from the shell, never a
    // half-written body -- the branch no suite reaches through the live app.
    [Fact]
    public async Task Invoke_NextThrowsUnhandled_RendersA500FromTheShell()
    {
        var middleware = new ErrorPageMiddleware(
            _ => throw new InvalidOperationException("boom"),
            shell: new StaticErrorShell("<h1>{{title}}</h1><p>{{message}}</p>"),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<ErrorPageMiddleware>.Instance);

        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        string html = System.Text.Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("<h1>Something went wrong</h1>", html, StringComparison.Ordinal);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }
}
