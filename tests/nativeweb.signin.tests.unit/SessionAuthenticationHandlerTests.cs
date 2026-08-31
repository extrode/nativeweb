using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NativeWeb.SignIn;

using Xunit;

using static NativeWeb.SignIn.Tests.Unit.OAuthClientFixtures;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class SessionAuthenticationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProviderUser User = new("subject-1", "a@example.test", null);
    private static readonly ProviderTokens Tokens = new("access-1", "refresh-1", 1800);

    [Fact]
    public async Task AuthenticateAsync_NoCookie_IsAnonymousAndWritesNothing()
    {
        Harness harness = await Signed();
        var context = new DefaultHttpContext();
        SessionAuthenticationHandler handler = await harness.HandlerFor(context);

        AuthenticateResult result = await handler.AuthenticateAsync();

        Assert.True(result.None);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task AuthenticateAsync_ValidSession_CarriesTheSubjectAsNameIdentifier()
    {
        Harness harness = await Signed();
        SessionAuthenticationHandler handler = await harness.HandlerFor(harness.Request());

        AuthenticateResult result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(
            "subject-1",
            result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(SessionAuthenticationHandler.SchemeName, result.Ticket!.AuthenticationScheme);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidSession_CarriesNoOtherClaim()
    {
        Harness harness = await Signed();
        SessionAuthenticationHandler handler = await harness.HandlerFor(harness.Request());

        AuthenticateResult result = await handler.AuthenticateAsync();

        Assert.Equal(ClaimTypes.NameIdentifier, Assert.Single(result.Principal!.Claims).Type);
    }

    [Fact]
    public async Task AuthenticateAsync_UnknownCookie_IsAnonymousAndExpiresIt()
    {
        Harness harness = await Signed();
        DefaultHttpContext context = harness.Request();
        context.Request.Headers.Cookie = $"{harness.Options.CookieName}=nothing-this-host-issued";
        SessionAuthenticationHandler handler = await harness.HandlerFor(context);

        AuthenticateResult result = await handler.AuthenticateAsync();

        Assert.True(result.None);
        Assert.Contains(
            "expires=Thu, 01 Jan 1970",
            Assert.Single(context.Response.Headers.SetCookie!)!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticateAsync_UnknownCookie_NeverFails()
    {
        Harness harness = await Signed();
        DefaultHttpContext context = harness.Request();
        context.Request.Headers.Cookie = $"{harness.Options.CookieName}=nothing-this-host-issued";
        SessionAuthenticationHandler handler = await harness.HandlerFor(context);

        AuthenticateResult result = await handler.AuthenticateAsync();

        Assert.True(result.None);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task AuthenticateAsync_ExpiredSession_IsAnonymousAndExpiresTheCookie()
    {
        Harness harness = await Signed();
        harness.Clock.Now = Now.AddDays(15);
        DefaultHttpContext context = harness.Request();
        SessionAuthenticationHandler handler = await harness.HandlerFor(context);

        AuthenticateResult result = await handler.AuthenticateAsync();

        Assert.True(result.None);
        Assert.Contains(
            "expires=Thu, 01 Jan 1970",
            Assert.Single(context.Response.Headers.SetCookie!)!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticateAsync_ProviderRefusesTheRefresh_IsAnonymous()
    {
        Harness harness = await Signed(
            () => Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}"));
        harness.Clock.Now = Now.AddSeconds(1800);
        SessionAuthenticationHandler handler = await harness.HandlerFor(harness.Request());

        AuthenticateResult result = await handler.AuthenticateAsync();

        Assert.True(result.None);
        Assert.Equal(0, harness.Store.Count);
    }

    [Fact]
    public async Task AuthenticateAsync_TokenInsideTheSkew_RefreshesAndStaysSignedIn()
    {
        Harness harness = await Signed();
        harness.Clock.Now = Now.AddSeconds(1800);
        SessionAuthenticationHandler handler = await harness.HandlerFor(harness.Request());

        AuthenticateResult result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("subject-1", result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task SignOutAsync_RemovesTheStoredSession()
    {
        Harness harness = await Signed();
        SessionAuthenticationHandler handler = await harness.HandlerFor(harness.Request());

        await handler.SignOutAsync(null);

        Assert.Equal(0, harness.Store.Count);
    }

    [Fact]
    public async Task SignOutAsync_LeavesThisRequestAnonymous()
    {
        Harness harness = await Signed();
        DefaultHttpContext context = harness.Request();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "subject-1")], "test"));
        SessionAuthenticationHandler handler = await harness.HandlerFor(context);

        await handler.SignOutAsync(null);

        Assert.False(context.User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task SignOutAsync_WritesTheSameSetCookieAsRevoking()
    {
        Harness harness = await Signed();
        DefaultHttpContext throughTheHandler = harness.Request();
        SessionAuthenticationHandler handler = await harness.HandlerFor(throughTheHandler);
        await handler.SignOutAsync(null);

        Harness other = await Signed();
        DefaultHttpContext throughTheManager = other.Request();
        await other.Manager.RevokeAsync(throughTheManager, default);

        Assert.Equal(
            Assert.Single(throughTheManager.Response.Headers.SetCookie!),
            Assert.Single(throughTheHandler.Response.Headers.SetCookie!));
    }

    [Fact]
    public async Task SignOutAsync_MarksTheResponseNoStore()
    {
        Harness harness = await Signed();
        DefaultHttpContext context = harness.Request();
        SessionAuthenticationHandler handler = await harness.HandlerFor(context);

        await handler.SignOutAsync(null);

        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task SignOutAsync_KeepsAConsumersOwnCacheDirectives()
    {
        Harness harness = await Signed();
        DefaultHttpContext context = harness.Request();
        context.Response.Headers.CacheControl = "no-cache, must-revalidate, max-age=0";
        SessionAuthenticationHandler handler = await harness.HandlerFor(context);

        await handler.SignOutAsync(null);

        Assert.Equal(
            "no-cache, must-revalidate, max-age=0, no-store",
            context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task SignOutAsync_DoesNotRepeatNoStore()
    {
        Harness harness = await Signed();
        DefaultHttpContext context = harness.Request();
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        SessionAuthenticationHandler handler = await harness.HandlerFor(context);

        await handler.SignOutAsync(null);

        Assert.Equal("no-store, max-age=0", context.Response.Headers.CacheControl);
    }

    private static async Task<Harness> Signed(Func<HttpResponseMessage>? providerResponse = null)
    {
        SignInOptions options = Options();
        var clock = new MovableClock(Now);
        var store = new InMemorySessionStore(clock);
        var handlerStub = new StubProvider(
            providerResponse
            ?? (() => Json(
                HttpStatusCode.OK,
                "{\"access_token\":\"access-2\",\"refresh_token\":\"refresh-2\",\"expires_in\":1800}")));

        var manager = new SessionManager(
            store,
            new OAuthClient(new HttpClient(handlerStub), options, NullLogger<OAuthClient>.Instance),
            options,
            clock,
            NullLogger<SessionManager>.Instance);

        string cookieValue = await manager.MintAsync(new DefaultHttpContext(), User, Tokens, default);

        return new Harness(manager, store, options, clock, cookieValue);
    }

    private sealed record Harness(
        SessionManager Manager,
        InMemorySessionStore Store,
        SignInOptions Options,
        MovableClock Clock,
        string CookieValue)
    {
        internal DefaultHttpContext Request()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Cookie = $"{Options.CookieName}={CookieValue}";
            return context;
        }

        internal async Task<SessionAuthenticationHandler> HandlerFor(HttpContext context)
        {
            var handler = new SessionAuthenticationHandler(
                new SchemeOptions(),
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                Manager);

            await handler.InitializeAsync(
                new AuthenticationScheme(
                    SessionAuthenticationHandler.SchemeName,
                    null,
                    typeof(SessionAuthenticationHandler)),
                context);

            return handler;
        }
    }

    private sealed class SchemeOptions : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();

        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubProvider(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response());
    }
}
