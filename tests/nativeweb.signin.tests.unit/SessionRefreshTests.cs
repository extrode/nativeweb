using System.Net;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using NativeWeb.Security;
using NativeWeb.SignIn;

using Xunit;

using static NativeWeb.SignIn.Tests.Unit.OAuthClientFixtures;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class SessionRefreshTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProviderUser User = new("subject-1", "a@example.test", null);
    private static readonly ProviderTokens Tokens = new("access-1", "refresh-1", 1800);

    [Fact]
    public async Task EnsureFreshAsync_TokenWellInsideItsLife_DoesNotCallTheProvider()
    {
        var provider = new CountingProvider();
        Fixture fixture = await Signed(provider);

        SessionState after = await fixture.Manager.EnsureFreshAsync(fixture.Request(), fixture.State, default);

        Assert.Equal(0, provider.Calls);
        Assert.Equal(SessionRead.Valid, after.Read);
        Assert.Equal(fixture.State.Record, after.Record);
    }

    [Fact]
    public async Task EnsureFreshAsync_InsideTheSkew_RefreshesWithNoRedirect()
    {
        var provider = new CountingProvider();
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800) - TimeSpan.FromSeconds(30);
        DefaultHttpContext context = fixture.Request();

        SessionState after = await fixture.Manager.EnsureFreshAsync(context, fixture.State, default);

        Assert.Equal(1, provider.Calls);
        Assert.Equal(SessionRead.Valid, after.Read);
        Assert.Equal("subject-1", after.Subject);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task EnsureFreshAsync_RefreshedSession_StoresTheRotatedPair()
    {
        var provider = new CountingProvider();
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800);

        SessionState after = await fixture.Manager.EnsureFreshAsync(fixture.Request(), fixture.State, default);

        var protector = new StoreDataProtector(fixture.Options.StoreEncryptionKey!);
        Assert.Equal("access-2", protector.Unprotect(after.Record!.ProtectedAccessToken));
        Assert.Equal("refresh-2", protector.Unprotect(after.Record.ProtectedRefreshToken));
        Assert.Equal(OpaqueTokens.Hash("refresh-2"), after.Record.RefreshTokenHash);
        Assert.Equal(fixture.Clock.Now.AddSeconds(1800), after.Record.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task EnsureFreshAsync_ProviderRefuses_EndsTheSession()
    {
        var provider = new CountingProvider { Response = () => Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}") };
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800);
        DefaultHttpContext context = fixture.Request();

        SessionState after = await fixture.Manager.EnsureFreshAsync(context, fixture.State, default);

        Assert.Equal(SessionRead.Unknown, after.Read);
        Assert.Equal(0, fixture.Store.Count);
        Assert.Contains("expires=Thu, 01 Jan 1970", Assert.Single(context.Response.Headers.SetCookie!)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureFreshAsync_ProviderUnreachable_KeepsTheSession()
    {
        var provider = new CountingProvider { Response = () => Json(HttpStatusCode.BadGateway, "{}") };
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800);

        SessionState after = await fixture.Manager.EnsureFreshAsync(fixture.Request(), fixture.State, default);

        Assert.Equal(SessionRead.Valid, after.Read);
        Assert.Equal(1, fixture.Store.Count);
    }

    [Fact]
    public async Task EnsureFreshAsync_SessionRemovedMidFlight_IsUnknown()
    {
        var provider = new CountingProvider();
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800);
        await fixture.Store.RemoveAsync(fixture.State.KeyHash!, default);

        SessionState after = await fixture.Manager.EnsureFreshAsync(fixture.Request(), fixture.State, default);

        Assert.Equal(SessionRead.Unknown, after.Read);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task EnsureFreshAsync_TwoConcurrentRequests_CallTheProviderOnce()
    {
        var provider = new CountingProvider { Delay = TimeSpan.FromMilliseconds(50) };
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800);

        Task<SessionState> first = Task.Run(
            () => fixture.Manager.EnsureFreshAsync(fixture.Request(), fixture.State, default));
        Task<SessionState> second = Task.Run(
            () => fixture.Manager.EnsureFreshAsync(fixture.Request(), fixture.State, default));

        SessionState[] outcomes = await Task.WhenAll(first, second);

        Assert.Equal(1, provider.Calls);
        Assert.All(outcomes, state => Assert.Equal(SessionRead.Valid, state.Read));
        Assert.Equal(outcomes[0].Record, outcomes[1].Record);

        var protector = new StoreDataProtector(fixture.Options.StoreEncryptionKey!);
        Assert.All(outcomes, state => Assert.Equal("access-2", protector.Unprotect(state.Record!.ProtectedAccessToken)));
    }

    [Fact]
    public async Task EnsureFreshAsync_AnotherInstanceRefreshedFirst_UsesTheWinnersTokens()
    {
        var provider = new CountingProvider();
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800);

        // What a second process would have written while this one was talking to the provider.
        var protector = new StoreDataProtector(fixture.Options.StoreEncryptionKey!);
        SessionRecord winner = fixture.State.Record! with
        {
            ProtectedAccessToken = protector.Protect("access-from-elsewhere"),
            ProtectedRefreshToken = protector.Protect("refresh-from-elsewhere"),
            AccessTokenExpiresAt = fixture.Clock.Now.AddSeconds(1800),
            RefreshTokenHash = OpaqueTokens.Hash("refresh-from-elsewhere"),
        };
        provider.OnCall = () => fixture.Store.CreateAsync(fixture.State.KeyHash!, winner, default).GetAwaiter().GetResult();

        SessionState after = await fixture.Manager.EnsureFreshAsync(fixture.Request(), fixture.State, default);

        Assert.Equal(1, provider.Calls);
        Assert.Equal("access-from-elsewhere", protector.Unprotect(after.Record!.ProtectedAccessToken));
    }

    [Fact]
    public async Task EnsureFreshAsync_ConflictWinnerIsItselfStale_RefreshesAgain()
    {
        var provider = new CountingProvider();
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800);

        var protector = new StoreDataProtector(fixture.Options.StoreEncryptionKey!);

        // The winner of the compare-and-swap holds a token that has ALREADY expired -- what a
        // provider issuing a short-lived token, or a token response with no expires_in at all,
        // leaves behind. Returning it unexamined hands the caller a record NeedsRefresh rejects.
        SessionRecord stale = fixture.State.Record! with
        {
            ProtectedAccessToken = protector.Protect("access-stale"),
            ProtectedRefreshToken = protector.Protect("refresh-stale"),
            AccessTokenExpiresAt = fixture.Clock.Now.AddSeconds(-1),
            RefreshTokenHash = OpaqueTokens.Hash("refresh-stale"),
        };

        bool planted = false;
        provider.OnCall = () =>
        {
            if (planted)
            {
                return;
            }

            planted = true;
            fixture.Store.CreateAsync(fixture.State.KeyHash!, stale, default).GetAwaiter().GetResult();
        };

        SessionState after = await fixture.Manager.EnsureFreshAsync(fixture.Request(), fixture.State, default);

        Assert.Equal(2, provider.Calls);
        Assert.Equal("access-2", protector.Unprotect(after.Record!.ProtectedAccessToken));
    }

    [Fact]
    public async Task AccessToken_ExpiredWithNoRefreshTokenToUse_IsNotServed()
    {
        var provider = new CountingProvider();
        Fixture fixture = await Signed(provider, retainTokens: true, refreshToken: null);

        Assert.Equal("access-1", fixture.Manager.AccessToken(fixture.State.Record!));

        fixture.Clock.Now = Now.AddSeconds(1800);
        SessionState after = await fixture.Manager.EnsureFreshAsync(fixture.Request(), fixture.State, default);

        // The SESSION survives -- who the visitor is did not expire. The credential did.
        Assert.Equal(SessionRead.Valid, after.Read);
        Assert.Equal(0, provider.Calls);
        Assert.Null(fixture.Manager.AccessToken(after.Record!));
    }

    [Fact]
    public void TakeGate_WhileAnotherRequestHoldsOne_KeepsTheSameGate()
    {
        // The defect this replaced: the holder released, saw CurrentCount == 1 -- the count a
        // semaphore also shows while a thread that already has the gate has not yet reached
        // WaitAsync -- and removed it, so the next arrival built a SECOND gate for one session and
        // both refreshed at once.
        SessionManager.Gate first = SessionManager.TakeGate("key-hash");
        SessionManager.Gate second = SessionManager.TakeGate("key-hash");

        Assert.Same(first, second);

        SessionManager.DropGate("key-hash", first);

        SessionManager.Gate third = SessionManager.TakeGate("key-hash");
        Assert.Same(second, third);

        SessionManager.DropGate("key-hash", second);
        SessionManager.DropGate("key-hash", third);
    }

    [Fact]
    public void DropGate_ByTheLastUser_LeavesNothingBehind()
    {
        SessionManager.Gate gate = SessionManager.TakeGate("lonely-key");

        Assert.True(SessionManager.HasGate("lonely-key"));

        SessionManager.DropGate("lonely-key", gate);

        Assert.False(SessionManager.HasGate("lonely-key"));
    }

    [Fact]
    public async Task EnsureFreshAsync_AfterARefresh_LeavesNoGateBehind()
    {
        var provider = new CountingProvider();
        Fixture fixture = await Signed(provider);
        fixture.Clock.Now = Now.AddSeconds(1800);

        await fixture.Manager.EnsureFreshAsync(fixture.Request(), fixture.State, default);

        Assert.False(SessionManager.HasGate(fixture.State.KeyHash!));
    }

    [Fact]
    public async Task EnsureFreshAsync_WithRetainTokensOff_NeverCallsTheProvider()
    {
        var provider = new CountingProvider();
        Fixture fixture = await Signed(provider, retainTokens: false);
        fixture.Clock.Now = Now.AddDays(1);

        SessionState after = await fixture.Manager.EnsureFreshAsync(fixture.Request(), fixture.State, default);

        Assert.Equal(0, provider.Calls);
        Assert.Equal(SessionRead.Valid, after.Read);
    }

    [Fact]
    public async Task EnsureFreshAsync_UnknownSession_IsLeftAlone()
    {
        var provider = new CountingProvider();
        Fixture fixture = await Signed(provider);

        SessionState after =
            await fixture.Manager.EnsureFreshAsync(fixture.Request(), SessionState.Unknown, default);

        Assert.Equal(SessionRead.Unknown, after.Read);
        Assert.Equal(0, provider.Calls);
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
        SessionState state = await manager.ReadAsync(WithCookie(options, cookieValue), default);

        return new Fixture(manager, store, options, clock, cookieValue, state);
    }

    private static DefaultHttpContext WithCookie(SignInOptions options, string cookieValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{options.CookieName}={cookieValue}";
        return context;
    }

    private sealed record Fixture(
        SessionManager Manager,
        InMemorySessionStore Store,
        SignInOptions Options,
        MovableClock Clock,
        string CookieValue,
        SessionState State)
    {
        internal DefaultHttpContext Request() => WithCookie(Options, CookieValue);
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

        public Action? OnCall { get; set; }

        public Func<HttpResponseMessage> Response { get; set; } =
            () => Json(
                HttpStatusCode.OK,
                "{\"access_token\":\"access-2\",\"refresh_token\":\"refresh-2\",\"expires_in\":1800}");

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            OnCall?.Invoke();

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return Response();
        }
    }
}
