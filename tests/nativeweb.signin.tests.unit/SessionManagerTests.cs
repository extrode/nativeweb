using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using System.Security.Cryptography;

using NativeWeb.Security;
using NativeWeb.SignIn;

using Xunit;

using static NativeWeb.SignIn.Tests.Unit.OAuthClientFixtures;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class SessionManagerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProviderUser User = new("subject-1", "a@example.test", "A Person");
    private static readonly ProviderTokens Tokens = new("access-plaintext", "refresh-plaintext", 1800);

    [Fact]
    public async Task MintAsync_WritesACookieCarryingAnOpaqueValue()
    {
        (SessionManager manager, RecordingStore store, SignInOptions options) = Subject();
        DefaultHttpContext context = Context();

        string cookieValue = await manager.MintAsync(context, User, Tokens, default);

        Assert.Equal(43, cookieValue.Length);
        string setCookie = Assert.Single(context.Response.Headers.SetCookie!)!;
        Assert.StartsWith($"{options.CookieName}={cookieValue};", setCookie, StringComparison.Ordinal);
        Assert.DoesNotContain("subject-1", setCookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MintAsync_CookieCarriesTheFixedSecurityAttributes()
    {
        (SessionManager manager, _, _) = Subject();
        DefaultHttpContext context = Context();

        await manager.MintAsync(context, User, Tokens, default);

        string setCookie = Assert.Single(context.Response.Headers.SetCookie!)!;
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MintAsync_StoresNothingThatYieldsTheCookieOrTheTokens()
    {
        (SessionManager manager, RecordingStore store, _) = Subject();

        string cookieValue = await manager.MintAsync(Context(), User, Tokens, default);

        foreach (string field in store.Dump())
        {
            Assert.NotEqual(cookieValue, field);
            Assert.DoesNotContain(cookieValue, field, StringComparison.Ordinal);
            Assert.DoesNotContain("access-plaintext", field, StringComparison.Ordinal);
            Assert.DoesNotContain("refresh-plaintext", field, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task MintAsync_KeysTheRowByTheHashOfTheCookieValue()
    {
        (SessionManager manager, RecordingStore store, _) = Subject();

        string cookieValue = await manager.MintAsync(Context(), User, Tokens, default);

        Assert.Equal(OpaqueTokens.Hash(cookieValue), Assert.Single(store.Rows).Key);
    }

    [Fact]
    public async Task MintAsync_TokensAreRecoverableOnlyThroughTheProtector()
    {
        (SessionManager manager, RecordingStore store, SignInOptions options) = Subject();

        await manager.MintAsync(Context(), User, Tokens, default);

        SessionRecord row = Assert.Single(store.Rows).Value;
        var protector = new StoreDataProtector(options.StoreEncryptionKey!);
        Assert.Equal("access-plaintext", protector.Unprotect(row.ProtectedAccessToken));
        Assert.Equal("refresh-plaintext", protector.Unprotect(row.ProtectedRefreshToken));
        Assert.Equal(OpaqueTokens.Hash("refresh-plaintext"), row.RefreshTokenHash);
        Assert.Equal(Now.AddSeconds(1800), row.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task MintAsync_WithRetainTokensOff_StoresNoTokensAtAll()
    {
        (SessionManager manager, RecordingStore store, _) = Subject(retainTokens: false);

        await manager.MintAsync(Context(), User, Tokens, default);

        SessionRecord row = Assert.Single(store.Rows).Value;
        Assert.Equal("", row.ProtectedAccessToken);
        Assert.Equal("", row.ProtectedRefreshToken);
        Assert.Equal("", row.RefreshTokenHash);
    }

    [Fact]
    public async Task MintAsync_WithoutARefreshToken_StoresNoRefreshMaterial()
    {
        (SessionManager manager, RecordingStore store, _) = Subject();

        await manager.MintAsync(Context(), User, new ProviderTokens("access-plaintext", null, 60), default);

        SessionRecord row = Assert.Single(store.Rows).Value;
        Assert.Equal("", row.ProtectedRefreshToken);
        Assert.Equal("", row.RefreshTokenHash);
    }

    [Fact]
    public async Task ReadAsync_NoCookie_IsUnknown()
    {
        (SessionManager manager, _, _) = Subject();

        SessionState state = await manager.ReadAsync(Context(), default);

        Assert.Equal(SessionRead.Unknown, state.Read);
        Assert.Null(state.Subject);
    }

    [Fact]
    public async Task ReadAsync_UnknownCookieValue_IsUnknown()
    {
        (SessionManager manager, _, SignInOptions options) = Subject();

        SessionState state = await manager.ReadAsync(Context(options.CookieName, OpaqueTokens.Generate()), default);

        Assert.Equal(SessionRead.Unknown, state.Read);
    }

    [Fact]
    public async Task ReadAsync_LiveSession_IsValidAndCarriesTheSubject()
    {
        (SessionManager manager, _, SignInOptions options) = Subject();
        string cookieValue = await manager.MintAsync(Context(), User, Tokens, default);

        SessionState state = await manager.ReadAsync(Context(options.CookieName, cookieValue), default);

        Assert.Equal(SessionRead.Valid, state.Read);
        Assert.Equal("subject-1", state.Subject);
        Assert.NotNull(state.Record);
    }

    [Fact]
    public async Task ReadAsync_AlteredCookieValue_IsRefused()
    {
        (SessionManager manager, _, SignInOptions options) = Subject();
        string cookieValue = await manager.MintAsync(Context(), User, Tokens, default);
        string altered = (cookieValue[0] == 'a' ? 'b' : 'a') + cookieValue[1..];

        SessionState state = await manager.ReadAsync(Context(options.CookieName, altered), default);

        Assert.Equal(SessionRead.Unknown, state.Read);
        Assert.Null(state.Subject);
    }

    [Fact]
    public async Task ReadAsync_PastItsLifetime_IsExpiredAndDistinguishableFromUnknown()
    {
        var clock = new MovableClock(Now);
        (SessionManager manager, _, SignInOptions options) = Subject(clock: clock);
        string cookieValue = await manager.MintAsync(Context(), User, Tokens, default);

        clock.Now = Now + options.SessionLifetime + TimeSpan.FromSeconds(1);
        SessionState state = await manager.ReadAsync(Context(options.CookieName, cookieValue), default);

        Assert.Equal(SessionRead.Expired, state.Read);
        Assert.Null(state.Subject);
    }

    [Fact]
    public async Task ReadAsync_ExpiredButUnswept_IsStillRefused()
    {
        var clock = new MovableClock(Now);
        (SessionManager manager, RecordingStore store, SignInOptions options) = Subject(clock: clock);
        string cookieValue = await manager.MintAsync(Context(), User, Tokens, default);

        clock.Now = Now.AddDays(400);

        Assert.Single(store.Rows);
        Assert.Equal(SessionRead.Expired, (await manager.ReadAsync(Context(options.CookieName, cookieValue), default)).Read);
    }

    [Fact]
    public async Task ReadAsync_TokensUnderARetiredKey_IsUnknownRatherThanThrowing()
    {
        var store = new RecordingStore();
        (SessionManager minted, _, SignInOptions options) = Subject(sharedStore: store);
        string cookieValue = await minted.MintAsync(Context(), User, Tokens, default);

        (SessionManager rekeyed, _, SignInOptions rekeyedOptions) = Subject(
            sharedStore: store, storeKey: RandomNumberGenerator.GetBytes(StoreDataProtector.KeyBytes));

        SessionState state = await rekeyed.ReadAsync(Context(rekeyedOptions.CookieName, cookieValue), default);

        Assert.Equal(SessionRead.Unknown, state.Read);
        Assert.Null(state.Subject);
    }

    [Fact]
    public async Task ReadAsync_TokensUnderARetiredKey_RemovesTheRowAndExpiresTheCookie()
    {
        var store = new RecordingStore();
        (SessionManager minted, _, _) = Subject(sharedStore: store);
        string cookieValue = await minted.MintAsync(Context(), User, Tokens, default);

        (SessionManager rekeyed, _, SignInOptions options) = Subject(
            sharedStore: store, storeKey: RandomNumberGenerator.GetBytes(StoreDataProtector.KeyBytes));
        DefaultHttpContext context = Context(options.CookieName, cookieValue);

        await rekeyed.ReadAsync(context, default);

        Assert.Empty(store.Rows);
        string setCookie = Assert.Single(context.Response.Headers.SetCookie!)!;
        Assert.Contains("expires=Thu, 01 Jan 1970", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_RetiredKeyButNoTokensRetained_IsStillValid()
    {
        var store = new RecordingStore();
        (SessionManager minted, _, _) = Subject(retainTokens: false, sharedStore: store);
        string cookieValue = await minted.MintAsync(Context(), User, Tokens, default);

        (SessionManager rekeyed, _, SignInOptions options) = Subject(
            retainTokens: false,
            sharedStore: store,
            storeKey: RandomNumberGenerator.GetBytes(StoreDataProtector.KeyBytes));

        SessionState state = await rekeyed.ReadAsync(Context(options.CookieName, cookieValue), default);

        // Who the visitor is was settled at sign-in and is not encrypted. A host that retains no
        // tokens has nothing to fail to decrypt, so a key change must not sign everyone out.
        Assert.Equal(SessionRead.Valid, state.Read);
        Assert.Equal("subject-1", state.Subject);
    }

    [Fact]
    public async Task RevokeAsync_RemovesTheRowAndExpiresTheCookie()
    {
        (SessionManager manager, RecordingStore store, SignInOptions options) = Subject();
        string cookieValue = await manager.MintAsync(Context(), User, Tokens, default);
        DefaultHttpContext context = Context(options.CookieName, cookieValue);

        await manager.RevokeAsync(context, default);

        Assert.Empty(store.Rows);
        string setCookie = Assert.Single(context.Response.Headers.SetCookie!)!;
        Assert.Contains("expires=Thu, 01 Jan 1970", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RevokeAsync_WithoutACookie_StillClearsAndRemovesNothing()
    {
        (SessionManager manager, RecordingStore store, _) = Subject();
        DefaultHttpContext context = Context();

        await manager.RevokeAsync(context, default);

        Assert.Empty(store.Removed);
        Assert.Single(context.Response.Headers.SetCookie!);
    }

    [Fact]
    public async Task ClearCookie_AndRevokeAsync_ProduceTheSameHeader()
    {
        (SessionManager manager, _, SignInOptions options) = Subject();
        string cookieValue = await manager.MintAsync(Context(), User, Tokens, default);

        DefaultHttpContext revoked = Context(options.CookieName, cookieValue);
        await manager.RevokeAsync(revoked, default);

        DefaultHttpContext cleared = Context(options.CookieName, cookieValue);
        manager.ClearCookie(cleared);

        Assert.Equal(revoked.Response.Headers.SetCookie, cleared.Response.Headers.SetCookie);
    }

    [Fact]
    public async Task AccessToken_ReturnsThePlaintextForALiveSession()
    {
        (SessionManager manager, _, SignInOptions options) = Subject();
        string cookieValue = await manager.MintAsync(Context(), User, Tokens, default);
        SessionState state = await manager.ReadAsync(Context(options.CookieName, cookieValue), default);

        Assert.Equal("access-plaintext", manager.AccessToken(state.Record!));
    }

    [Fact]
    public async Task AccessToken_WithRetainTokensOff_IsNull()
    {
        (SessionManager manager, _, SignInOptions options) = Subject(retainTokens: false);
        string cookieValue = await manager.MintAsync(Context(), User, Tokens, default);
        SessionState state = await manager.ReadAsync(Context(options.CookieName, cookieValue), default);

        Assert.Null(manager.AccessToken(state.Record!));
    }

    private static (SessionManager Manager, RecordingStore Store, SignInOptions Options) Subject(
        bool retainTokens = true,
        MovableClock? clock = null,
        RecordingStore? sharedStore = null,
        byte[]? storeKey = null)
    {
        SignInOptions options = OAuthClientFixtures.Options();
        options.RetainTokens = retainTokens;

        if (storeKey is not null)
        {
            options.StoreEncryptionKey = storeKey;
        }

        // Supplied by the caller when a second manager has to see rows the first one wrote: that
        // is the whole of a key change, one store read by two sets of keys.
        RecordingStore store = sharedStore ?? new RecordingStore();

        return (
            new SessionManager(
                store,
                ClientFor(new StubHandler(Json(System.Net.HttpStatusCode.OK, "{}"))),
                options,
                clock ?? new MovableClock(Now),
                NullLogger<SessionManager>.Instance),
            store,
            options);
    }

    private static DefaultHttpContext Context(string? cookieName = null, string? cookieValue = null)
    {
        var context = new DefaultHttpContext();
        if (cookieName is not null && cookieValue is not null)
        {
            context.Request.Headers.Cookie = $"{cookieName}={cookieValue}";
        }

        return context;
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingStore : ISessionStore
    {
        public Dictionary<string, SessionRecord> Rows { get; } = new(StringComparer.Ordinal);

        public List<string> Removed { get; } = [];

        public IEnumerable<string> Dump() =>
            Rows.SelectMany(row => new[]
            {
                row.Key,
                row.Value.Subject,
                row.Value.ProtectedAccessToken,
                row.Value.ProtectedRefreshToken,
                row.Value.RefreshTokenHash,
            });

        public Task CreateAsync(string keyHash, SessionRecord record, CancellationToken ct)
        {
            Rows[keyHash] = record;
            return Task.CompletedTask;
        }

        public Task<SessionRecord?> GetAsync(string keyHash, CancellationToken ct) =>
            Task.FromResult(Rows.TryGetValue(keyHash, out SessionRecord? row) ? row : null);

        public Task<SessionReplace> ReplaceIfMatchAsync(
            string keyHash, string expectedRefreshTokenHash, SessionRecord replacement, CancellationToken ct)
        {
            if (!Rows.TryGetValue(keyHash, out SessionRecord? current))
            {
                return Task.FromResult(SessionReplace.Gone);
            }

            if (!string.Equals(current.RefreshTokenHash, expectedRefreshTokenHash, StringComparison.Ordinal))
            {
                return Task.FromResult(SessionReplace.Conflict);
            }

            Rows[keyHash] = replacement;
            return Task.FromResult(SessionReplace.Replaced);
        }

        public Task RemoveAsync(string keyHash, CancellationToken ct)
        {
            Removed.Add(keyHash);
            Rows.Remove(keyHash);
            return Task.CompletedTask;
        }

        public Task SweepAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
