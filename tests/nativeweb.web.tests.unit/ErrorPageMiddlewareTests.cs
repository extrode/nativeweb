using System.Net;

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
}
