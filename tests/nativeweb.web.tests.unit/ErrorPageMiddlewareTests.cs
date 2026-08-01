using System.Net;

using Microsoft.AspNetCore.Http;

using NativeWeb.Web.Middleware;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

public class ErrorPageMiddlewareTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound, "Not found")]
    [InlineData(HttpStatusCode.Unauthorized, "Sign in required")]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden")]
    [InlineData(HttpStatusCode.TooManyRequests, "Too many requests")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "Request too large")]
    public void Describe_KnownStatusCode_ReturnsExpectedTitle(HttpStatusCode statusCode, string expectedTitle)
    {
        (string? title, string _) = ErrorPageMiddleware.Describe((int)statusCode);

        Assert.Equal(expectedTitle, title);
    }

    // The BadHttpRequestException catch: Kestrel's own verdict on an over-limit
    // or malformed request must surface with its own status code, not be
    // swallowed by the generic catch as a 500. The 413 the e2e BodyLimitTests
    // observe is produced by Kestrel at the connection edge (the client sends
    // Expect: 100-continue), so this path -- the limit discovered mid-read,
    // inside the pipeline -- is only exercisable by invoking the middleware
    // directly. The htmx branch of WriteErrorAsync renders a bare fragment
    // without touching AssetUrls/IAntiforgery, so those can be null here.
    [Fact]
    public async Task Invoke_NextThrowsBadHttpRequest_SurfacesKestrelsStatusCode()
    {
        var middleware = new ErrorPageMiddleware(
            _ => throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            assets: null!,
            antiforgery: null!,
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

    [Fact]
    public void Describe_UnmappedStatusCode_FallsBackToGenericMessage()
    {
        (string? title, string? message) = ErrorPageMiddleware.Describe((int)HttpStatusCode.InternalServerError);

        Assert.Equal("Something went wrong", title);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }
}
