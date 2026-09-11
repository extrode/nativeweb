using System.Net;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using NativeWeb.SignIn;

using Xunit;

using static NativeWeb.SignIn.Tests.Unit.OAuthClientFixtures;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class SessionTokensTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProviderUser User = new("subject-1", "a@example.test", null);
    private static readonly ProviderTokens Tokens = new("access-1", "refresh-1", 1800);

    [Fact]
    public async Task GetAccessTokenAsync_LiveSession_HandsBackTheRetainedToken()
    {
        Fixture fixture = await Signed(new CountingProvider());

        Assert.Equal("access-1", await fixture.Tokens.GetAccessTokenAsync(fixture.Request(), default));
    }

    [Fact]
    public async Task GetAccessTokenAsync_NoCookie_IsNull()
    {
        Fixture fixture = await Signed(new CountingProvider());

        Assert.Null(await fixture.Tokens.GetAccessTokenAsync(new DefaultHttpContext(), default));
    }

    [Fact]
    public async Task GetAccessTokenAsync_ACookieThisHostNeverIssued_IsNull()
    {
        Fixture fixture = await Signed(new CountingProvider());
        DefaultHttpContext context = new();
        context.Request.Headers.Cookie = $"{fixture.Options.CookieName}=not-a-session";

        Assert.Null(await fixture.Tokens.GetAccessTokenAsync(context, default));
    }

    [Fact]
    public async Task GetAccessTokenAsync_SessionPastItsLifetime_IsNull()
    {
        Fixture fixture = await Signed(new CountingProvider());
        fixture.Clock.Now = Now + fixture.Options.SessionLifetime + TimeSpan.FromMinutes(1);

        Assert.Null(await fixture.Tokens.GetAccessTokenAsync(fixture.Request(), default));
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithRetainTokensOff_ThrowsNamingTheOption()
    {
        Fixture fixture = await Signed(new CountingProvider(), retainTokens: false);

        InvalidOperationException refusal =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.Tokens.GetAccessTokenAsync(fixture.Request(), default));

        Assert.Contains(nameof(SignInOptions.RetainTokens), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithRetainTokensOff_RefusesAnAnonymousRequestToo()
    {
        Fixture fixture = await Signed(new CountingProvider(), retainTokens: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Tokens.GetAccessTokenAsync(new DefaultHttpContext(), default));
    }

    [Fact]
    public async Task GetAccessTokenAsync_ProviderRefusesTheRefresh_LeavesTheRequestAnonymous()
    {
        var provider = new CountingProvider
        {
            Response = () => Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}"),
        };
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800);

        DefaultHttpContext context = fixture.Request();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "subject-1")], "TestScheme"));

        await fixture.Tokens.GetAccessTokenAsync(context, default);

        Assert.NotEqual(true, context.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenNothingEndedTheSession_LeavesTheUserAlone()
    {
        Fixture fixture = await Signed(new CountingProvider());
        DefaultHttpContext context = fixture.Request();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "subject-1")], "TestScheme"));
        context.User = principal;

        await fixture.Tokens.GetAccessTokenAsync(context, default);

        Assert.Same(principal, context.User);
    }

    [Fact]
    public async Task GetAccessTokenAsync_InsideTheSkew_RefreshesAndHandsBackTheNewToken()
    {
        var provider = new CountingProvider();
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800) - TimeSpan.FromSeconds(30);

        string? token = await fixture.Tokens.GetAccessTokenAsync(fixture.Request(), default);

        Assert.Equal("access-2", token);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_TwoConcurrentRequests_CallTheProviderOnce()
    {
        var provider = new CountingProvider { Delay = TimeSpan.FromMilliseconds(50) };
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800);

        Task<string?> first = Task.Run(
            () => fixture.Tokens.GetAccessTokenAsync(fixture.Request(), default));
        Task<string?> second = Task.Run(
            () => fixture.Tokens.GetAccessTokenAsync(fixture.Request(), default));

        string?[] tokens = await Task.WhenAll(first, second);

        Assert.Equal(1, provider.Calls);
        Assert.All(tokens, token => Assert.Equal("access-2", token));
    }

    [Fact]
    public async Task GetAccessTokenAsync_ProviderRefusesTheRefresh_IsNullAndEndsTheSession()
    {
        var provider = new CountingProvider
        {
            Response = () => Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}"),
        };
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800);
        DefaultHttpContext context = fixture.Request();

        Assert.Null(await fixture.Tokens.GetAccessTokenAsync(context, default));
        Assert.Equal(0, fixture.Store.Count);
        Assert.Contains(
            "expires=Thu, 01 Jan 1970",
            Assert.Single(context.Response.Headers.SetCookie!)!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ProviderUnreachable_HandsBackTheTokenItHas()
    {
        var provider = new CountingProvider { Response = () => Json(HttpStatusCode.BadGateway, "{}") };
        Fixture fixture = await Signed(provider);

        // Inside the skew but not yet past expiry: the refresh fails, and the token in hand is
        // still one the provider will accept. Reporting null here would take a product off the air
        // for the whole of an outage that cost it nothing.
        fixture.Clock.Now = Now.AddSeconds(1800) - TimeSpan.FromSeconds(30);

        Assert.Equal("access-1", await fixture.Tokens.GetAccessTokenAsync(fixture.Request(), default));
        Assert.Equal(1, fixture.Store.Count);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ExpiredTokenAndNoRefreshToken_IsNullWhileTheSessionLives()
    {
        var provider = new CountingProvider();
        Fixture fixture = await Signed(provider, retainTokens: true, refreshToken: null);
        fixture.Clock.Now = Now.AddSeconds(1801);

        Assert.Null(await fixture.Tokens.GetAccessTokenAsync(fixture.Request(), default));
        Assert.Equal(0, provider.Calls);
        Assert.Equal(1, fixture.Store.Count);

        SessionState state = await fixture.Manager.ReadAsync(fixture.Request(), default);
        Assert.Equal(SessionRead.Valid, state.Read);
    }

    [Fact]
    public async Task GetAccessTokenAsync_NoContext_Throws()
    {
        Fixture fixture = await Signed(new CountingProvider());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => fixture.Tokens.GetAccessTokenAsync(null!, default));
    }

    private static Task<Fixture> Signed(CountingProvider provider, bool retainTokens = true) =>
        Signed(provider, retainTokens, Tokens.RefreshToken);

    private static async Task<Fixture> Signed(
        CountingProvider provider, bool retainTokens, string? refreshToken)
    {
        SignInOptions options = Options();
        options.RetainTokens = retainTokens;
        var clock = new MovableClock(Now);
        var store = new InMemorySessionStore(clock);
        var manager = new SessionManager(
            store,
            new OAuthClient(new HttpClient(provider), options, NullLogger<OAuthClient>.Instance),
            options,
            clock,
            NullLogger<SessionManager>.Instance);

        var minting = new DefaultHttpContext();
        string cookieValue = await manager.MintAsync(
            minting, User, Tokens with { RefreshToken = refreshToken }, default);

        return new Fixture(manager, new SessionTokens(manager), store, options, clock, cookieValue);
    }

    private sealed record Fixture(
        SessionManager Manager,
        ISessionTokens Tokens,
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
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CountingProvider : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public TimeSpan Delay { get; set; }

        public Func<HttpResponseMessage> Response { get; set; } =
            () => Json(
                HttpStatusCode.OK,
                "{\"access_token\":\"access-2\",\"refresh_token\":\"refresh-2\",\"expires_in\":1800}");

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return Response();
        }
    }
}
