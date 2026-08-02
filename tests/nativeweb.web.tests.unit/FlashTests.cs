using Microsoft.AspNetCore.Http;

using NativeWeb.Web.Auth;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

// Flash is a cookie roundtrip: Set writes the __Host- cookie on one response,
// the browser echoes it, Take on the next request reads it once and expires it.
// These tests drive that loop through real header serialization rather than
// mocking the cookie collections.
public class FlashTests
{
    private static string SetAndCaptureCookie(FlashKind kind, string message)
    {
        var context = new DefaultHttpContext();
        Flash.Set(context, kind, message);
        string setCookie = context.Response.Headers.SetCookie.ToString();
        Assert.NotEmpty(setCookie);
        return setCookie.Split(';')[0]; // "name=value"
    }

    private static DefaultHttpContext ContextWithCookie(string cookiePair)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = cookiePair;
        return context;
    }

    [Fact]
    public void SetThenTake_RoundTripsKindAndMessage()
    {
        string cookie = SetAndCaptureCookie(FlashKind.Success, "Your account is ready — you're signed in.");

        DefaultHttpContext next = ContextWithCookie(cookie);
        (bool Success, string Message)? flash = Flash.Take(next);

        Assert.NotNull(flash);
        Assert.True(flash.Value.Success);
        Assert.Equal("Your account is ready — you're signed in.", flash.Value.Message);
    }

    [Fact]
    public void SetThenTake_ErrorKind_RoundTrips()
    {
        string cookie = SetAndCaptureCookie(FlashKind.Error, "Something went wrong.");

        (bool Success, string Message)? flash = Flash.Take(ContextWithCookie(cookie));

        Assert.NotNull(flash);
        Assert.False(flash.Value.Success);
        Assert.Equal("Something went wrong.", flash.Value.Message);
    }

    [Fact]
    public void Set_EmitsHostPrefixCookieAttributes()
    {
        var context = new DefaultHttpContext();
        Flash.Set(context, FlashKind.Success, "ok");
        string setCookie = context.Response.Headers.SetCookie.ToString();

        Assert.StartsWith("__Host-nw-flash=", setCookie, StringComparison.Ordinal);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Take_ExpiresTheCookieInTheSameResponse()
    {
        string cookie = SetAndCaptureCookie(FlashKind.Success, "once");
        DefaultHttpContext next = ContextWithCookie(cookie);

        Flash.Take(next);

        string setCookie = next.Response.Headers.SetCookie.ToString();
        Assert.Contains("__Host-nw-flash=", setCookie, StringComparison.Ordinal);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Take_NoCookie_ReturnsNull()
    {
        Assert.Null(Flash.Take(new DefaultHttpContext()));
    }

    [Theory]
    [InlineData("__Host-nw-flash=garbage")]
    [InlineData("__Host-nw-flash=ok%3A")] // escaped prefix, not a real prefix
    [InlineData("__Host-nw-flash=ok:")] // empty message
    public void Take_UnparseableOrEmpty_ReturnsNull(string cookiePair)
    {
        Assert.Null(Flash.Take(ContextWithCookie(cookiePair)));
    }

    [Fact]
    public void Set_TruncatesOverlongMessages()
    {
        string cookie = SetAndCaptureCookie(FlashKind.Success, new string('x', 400));

        (bool Success, string Message)? flash = Flash.Take(ContextWithCookie(cookie));

        Assert.NotNull(flash);
        Assert.Equal(300, flash.Value.Message.Length);
    }

    [Fact]
    public void Set_TruncationBacksOffASplitSurrogatePair()
    {
        // 299 chars then an astral-plane pair straddling the 300 boundary: a
        // naive cut would strand the high surrogate and EscapeDataString throws.
        string message = new string('x', 299) + "\U0001F600" + new string('y', 50);

        string cookie = SetAndCaptureCookie(FlashKind.Success, message);
        (bool Success, string Message)? flash = Flash.Take(ContextWithCookie(cookie));

        Assert.NotNull(flash);
        Assert.Equal(new string('x', 299), flash.Value.Message);
    }
}
