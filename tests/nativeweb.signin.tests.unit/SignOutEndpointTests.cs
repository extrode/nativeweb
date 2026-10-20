using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NativeWeb.SignIn;

using Xunit;

using static NativeWeb.SignIn.Tests.Unit.OAuthClientFixtures;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class SignOutEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProviderUser User = new("subject-1", "a@example.test", null);
    private static readonly ProviderTokens Tokens = new("access-1", "refresh-1", 1800);

    [Fact]
    public async Task PostLogout_RedirectsToTheSignedOutPath()
    {
        Fixture fixture = await Signed();

        DefaultHttpContext context = await fixture.SignOut();

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal(
            fixture.Options.SignedOutPath,
            context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task PostLogout_RemovesTheStoredSession()
    {
        Fixture fixture = await Signed();

        await fixture.SignOut();

        Assert.Equal(0, fixture.Store.Count);
    }

    [Fact]
    public async Task PostLogout_ExpiresTheCookie()
    {
        Fixture fixture = await Signed();

        DefaultHttpContext context = await fixture.SignOut();

        Assert.Contains(
            "expires=Thu, 01 Jan 1970",
            Assert.Single(context.Response.Headers.SetCookie!)!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostLogout_MarksTheResponseNoStore()
    {
        Fixture fixture = await Signed();

        DefaultHttpContext context = await fixture.SignOut();

        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task PostLogout_WithoutAValidToken_IsRefused()
    {
        Fixture fixture = await Signed();

        DefaultHttpContext context = await fixture.SignOut(antiforgeryValid: false);

        Assert.Equal(400, context.Response.StatusCode);
    }

    [Fact]
    public async Task PostLogout_WithoutAValidToken_KeepsTheSession()
    {
        Fixture fixture = await Signed();

        DefaultHttpContext context = await fixture.SignOut(antiforgeryValid: false);

        Assert.Equal(1, fixture.Store.Count);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task PostLogout_NoSessionToEnd_StillRedirects()
    {
        Fixture fixture = await Signed();

        DefaultHttpContext context = await fixture.SignOut(withCookie: false);

        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal(1, fixture.Store.Count);
    }

    [Fact]
    public async Task PostLogout_WritesTheSameSetCookieAsTheAuthenticationHandler()
    {
        Fixture throughTheEndpoint = await Signed();
        DefaultHttpContext endpointContext = await throughTheEndpoint.SignOut();

        Fixture throughTheHandler = await Signed();
        DefaultHttpContext handlerContext = throughTheHandler.Request();
        var handler = new SessionAuthenticationHandler(
            new SchemeOptions(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            ManagerUnderTheDefaultScheme(throughTheHandler.Manager));
        await handler.InitializeAsync(
            new AuthenticationScheme(
                SessionAuthenticationHandler.SchemeName,
                null,
                typeof(SessionAuthenticationHandler)),
            handlerContext);
        await handler.SignOutAsync(null);

        Assert.Equal(
            Assert.Single(handlerContext.Response.Headers.SetCookie!),
            Assert.Single(endpointContext.Response.Headers.SetCookie!));
    }

    private static async Task<Fixture> Signed()
    {
        SignInOptions options = Options();
        var clock = new FixedClock(Now);
        var store = new InMemorySessionStore(clock);
        var manager = new SessionManager(
            store,
            new OAuthClient(new HttpClient(new RefusingHandler()), options, NullLogger<OAuthClient>.Instance),
            options,
            clock,
            NullLogger<SessionManager>.Instance);

        string cookieValue = await manager.MintAsync(new DefaultHttpContext(), User, Tokens, default);

        return new Fixture(manager, store, options, cookieValue);
    }

    private sealed record Fixture(
        SessionManager Manager,
        InMemorySessionStore Store,
        SignInOptions Options,
        string CookieValue)
    {
        internal DefaultHttpContext Request()
        {
            DefaultHttpContext context = new DefaultHttpContext().Executable();
            context.Request.Headers.Cookie = $"{Options.CookieName}={CookieValue}";
            return context;
        }

        internal async Task<DefaultHttpContext> SignOut(
            bool antiforgeryValid = true, bool withCookie = true)
        {
            DefaultHttpContext context = withCookie ? Request() : new DefaultHttpContext().Executable();

            IResult result = await SignInEndpoints.PostLogoutAsync(
                context,
                new StubAntiforgery(antiforgeryValid),
                Manager,
                Options,
                NullLoggerFactory.Instance);

            await result.ExecuteAsync(context);

            return context;
        }
    }

    private sealed class StubAntiforgery(bool valid) : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) =>
            throw new NotSupportedException();

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) =>
            throw new NotSupportedException();

        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(valid);

        public void SetCookieTokenAndHeader(HttpContext httpContext) =>
            throw new NotSupportedException();

        public Task ValidateRequestAsync(HttpContext httpContext) =>
            valid
                ? Task.CompletedTask
                : Task.FromException(new AntiforgeryValidationException("no token"));
    }

    private sealed class SchemeOptions : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();

        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Signing out must not call the provider.");
    }
}
