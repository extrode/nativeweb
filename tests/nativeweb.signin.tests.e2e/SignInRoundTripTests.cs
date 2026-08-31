using Microsoft.Playwright;

using Xunit;

namespace NativeWeb.SignIn.Tests.E2e;

/// <summary>
/// The redirect round trip in a real browser, against the NativeAOT binary (spec 016 R9, R26).
/// </summary>
/// <remarks>
/// <para>
/// The demo provider approves without asking, so a browser sent to <c>/account</c> with no
/// session walks the whole flow unattended: challenge, authorize, callback, code exchange,
/// session cookie, and back where it was going. That is the point of it — what this tier is
/// for is the browser's half, which no <see cref="System.Net.CookieContainer"/> reproduces.
/// </para>
/// <para>
/// It is also the only place the relying party runs inside a NativeAOT binary. The session
/// store, the state and nonce handling, the token exchange and the introspection filter are
/// all JIT-hosted in every tier below; a reflection-shaped call that trims away would fail
/// here first and in production otherwise.
/// </para>
/// </remarks>
[Collection(SignInE2eFixture.Name)]
public sealed class SignInRoundTripTests(SignInProcessFixture app, SignInBrowserFixture browsers)
{
    private const string SessionCookie = "__Host-signin";

    private const string Subject = "demo-subject";

    private async Task<IPage> FreshPageAsync()
    {
        IBrowserContext context = await browsers.Browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = app.BaseUrl, IgnoreHTTPSErrors = true });

        return await context.NewPageAsync();
    }

    private static async Task<BrowserContextCookiesResult?> SessionAsync(IBrowserContext context)
    {
        IReadOnlyList<BrowserContextCookiesResult> cookies = await context.CookiesAsync();

        return cookies.FirstOrDefault(cookie => cookie.Name == SessionCookie);
    }

    [Fact]
    public async Task TheRoundTrip_CompletesInABrowserAndArrivesWhereItWasGoing()
    {
        IPage page = await FreshPageAsync();

        await page.GotoAsync("/welcome");
        Assert.Equal("Anyone may read this", await page.InnerTextAsync("#welcome"));

        await page.ClickAsync("#to-account");

        Assert.Equal($"{app.BaseUrl}/account", page.Url);
        Assert.Equal(Subject, await page.InnerTextAsync("#subject"));
    }

    [Fact]
    public async Task TheSessionCookie_CarriesTheHostPrefixAndTheFlagsItImplies()
    {
        IPage page = await FreshPageAsync();
        await page.GotoAsync("/account");

        BrowserContextCookiesResult? session = await SessionAsync(page.Context);

        // The prefix is the assertion, not decoration on it: Chromium refuses to store a
        // __Host- cookie that is not Secure, or carries a Domain, or a Path other than "/",
        // so a cookie that is here at all has already been checked for three of the four.
        Assert.NotNull(session);
        Assert.True(session.HttpOnly, "The session cookie must be HttpOnly.");
        Assert.True(session.Secure, "The session cookie must be Secure.");
        Assert.Equal(SameSiteAttribute.Lax, session.SameSite);
        Assert.Equal("/", session.Path);
        Assert.NotEqual(Subject, session.Value);
    }

    [Fact]
    public async Task TheSession_SurvivesTheProviderReturnAndAnswersTheNextVisitWithoutOne()
    {
        IPage page = await FreshPageAsync();

        await page.GotoAsync("/account");
        string? first = (await SessionAsync(page.Context))?.Value;

        IResponse? second = await page.GotoAsync("/account");

        // Arrived directly. Without this the fact above would pass on an app that ran the
        // whole round trip again on every visit, which is a session that survives nothing.
        Assert.NotNull(second);
        Assert.Null(second.Request.RedirectedFrom);
        Assert.Equal(Subject, await page.InnerTextAsync("#subject"));
        Assert.Equal(first, (await SessionAsync(page.Context))?.Value);
    }

    [Fact]
    public async Task SignOut_LeavesTheNextVisitAnonymous()
    {
        IPage page = await FreshPageAsync();

        await page.GotoAsync("/account");
        string? before = (await SessionAsync(page.Context))?.Value;
        Assert.NotNull(before);

        await page.ClickAsync("#signout");

        Assert.Equal($"{app.BaseUrl}/welcome", page.Url);
        Assert.Null(await SessionAsync(page.Context));

        // The provider approves without asking, so the next visit signs in again rather than
        // stopping. What proves the old session is gone is that the new one is a different
        // opaque string -- a sign-out that cleared the browser's copy and left the store's
        // entry standing would hand back the same value.
        await page.GotoAsync("/account");
        Assert.NotEqual(before, (await SessionAsync(page.Context))?.Value);
    }
}
