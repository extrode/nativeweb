using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using NativeWeb.Security;

namespace NativeWeb.SignIn;

/// <summary>
/// Mints, reads and ends sessions. The cookie carries an opaque 256-bit value and nothing else;
/// everything about the session lives in the <see cref="ISessionStore"/>, keyed by the hash of
/// that value.
/// </summary>
/// <remarks>
/// The alternative — a signed ticket carrying the subject, or an encrypted token pair — needs no
/// store and was rejected: a refresh token that rotates on every use has to be replaced somewhere
/// two concurrent requests can serialize against, and a cookie is not that place. The value the
/// browser holds is therefore a lookup key, not a credential describing anything.
/// <para>
/// Nothing here is public that mints a session for an arbitrary subject. A public <c>Mint</c> on a
/// registered singleton is an authentication bypass a consumer can call with any subject it likes;
/// the testing package exposes minting behind its own reference, which a production project does
/// not carry.
/// </para>
/// </remarks>
/// <param name="store">Where sessions live. Supplied by the product.</param>
/// <param name="options">Validated options.</param>
/// <param name="clock">The clock, so expiry is testable.</param>
/// <param name="logger">This library's logger.</param>
public sealed partial class SessionManager(
    ISessionStore store,
    OAuthClient client,
    SignInOptions options,
    TimeProvider clock,
    ILogger<SessionManager> logger)
{
    /// <summary>
    /// One gate per session, so two requests on one session do not both call the provider.
    /// </summary>
    /// <remarks>
    /// The gate is in-process and the compare-and-swap is not: they cover different halves of the
    /// same race. Inside one process the gate means the provider is called ONCE and the second
    /// request reads the first one's result; across processes the gate does not exist, and
    /// <see cref="ISessionStore.ReplaceIfMatchAsync"/> is what stops the loser from writing over
    /// the winner. A rotating provider revokes the whole token family when a consumed refresh
    /// token is presented again, so a session survives only while both halves hold.
    /// <para>
    /// Static rather than an instance field: the gate has to be the same object for two concurrent
    /// requests, and a scoped registration would give each request its own manager and its own
    /// empty dictionary. Keyed by the row's key hash, so two sessions never wait on each other.
    /// </para>
    /// </remarks>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.Ordinal);

    private readonly StoreDataProtector _protector = new(options.StoreEncryptionKey
        ?? throw new InvalidOperationException(
            $"{nameof(SignInOptions.StoreEncryptionKey)} is unset. Call "
            + $"{nameof(SignInOptions)}.{nameof(SignInOptions.Validate)}() before constructing this."));

    /// <summary>
    /// Mints a session for a signed-in subject and writes the cookie.
    /// </summary>
    /// <param name="context">The request that just completed sign-in.</param>
    /// <param name="user">Who the provider says signed in.</param>
    /// <param name="tokens">
    /// The provider's tokens, or <see langword="null"/> when there are none to keep. Ignored
    /// entirely when <see cref="SignInOptions.RetainTokens"/> is off.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cookie value, for a caller that needs it. Secret; never logged.</returns>
    internal async Task<string> MintAsync(
        HttpContext context,
        ProviderUser user,
        ProviderTokens? tokens,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(user);

        DateTimeOffset now = clock.GetUtcNow();
        string cookieValue = OpaqueTokens.Generate();
        bool keep = options.RetainTokens && tokens is not null;

        var record = new SessionRecord(
            Subject: user.Subject,
            IssuedAt: now,
            ExpiresAt: now + options.SessionLifetime,
            ProtectedAccessToken: keep ? _protector.Protect(tokens!.AccessToken) : "",
            ProtectedRefreshToken: keep && tokens!.RefreshToken is { Length: > 0 } refresh
                ? _protector.Protect(refresh)
                : "",
            AccessTokenExpiresAt: keep ? now.AddSeconds(tokens!.ExpiresIn) : now,
            RefreshTokenHash: keep && tokens!.RefreshToken is { Length: > 0 } hashable
                ? OpaqueTokens.Hash(hashable)
                : "");

        await store.CreateAsync(OpaqueTokens.Hash(cookieValue), record, ct).ConfigureAwait(false);
        WriteCookie(context, cookieValue, record.ExpiresAt);
        LogSignedIn(logger, user.Subject);

        return cookieValue;
    }

    /// <summary>
    /// Reads the session the request carries.
    /// </summary>
    /// <remarks>
    /// Expiry is judged HERE and not left to <see cref="ISessionStore.SweepAsync"/>: a store that
    /// has not swept holds live rows past their expiry, and a reader that trusts the row's mere
    /// existence accepts them.
    /// </remarks>
    /// <param name="context">The incoming request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What the cookie turned out to be worth.</returns>
    internal async Task<SessionState> ReadAsync(HttpContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Cookies[options.CookieName] is not { Length: > 0 } cookieValue)
        {
            return SessionState.Unknown;
        }

        string keyHash = OpaqueTokens.Hash(cookieValue);
        SessionRecord? record = await store.GetAsync(keyHash, ct).ConfigureAwait(false);

        if (record is null)
        {
            // An unknown value and a forged one are the same thing here. That is what an opaque
            // token buys: there is nothing to tamper with, so there is no third answer to give.
            return SessionState.Unknown;
        }

        return record.ExpiresAt <= clock.GetUtcNow()
            ? new SessionState(SessionRead.Expired, null, null, keyHash)
            : new SessionState(SessionRead.Valid, record.Subject, record, keyHash);
    }

    /// <summary>
    /// Refreshes the session's access token when it is within <see cref="SignInOptions.RefreshSkew"/>
    /// of expiry, and returns the session to carry on with.
    /// </summary>
    /// <remarks>
    /// No redirect and no user-visible step: a request either finds a usable access token or gets
    /// one before it is served (R13). A provider that REFUSES the refresh ends the session — the
    /// alternative is a live session holding a token nothing will accept, which fails later, in a
    /// place with no way to recover. A provider that is merely UNREACHABLE does not: nothing was
    /// decided, and the existing token may well outlive the outage.
    /// </remarks>
    /// <param name="context">The request being served.</param>
    /// <param name="state">What <see cref="ReadAsync"/> returned.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The session as it stands after any refresh.</returns>
    internal async Task<SessionState> EnsureFreshAsync(
        HttpContext context,
        SessionState state,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (state.Read != SessionRead.Valid
            || state.Record is not { } record
            || state.KeyHash is not { } keyHash
            || !options.RetainTokens
            || record.ProtectedRefreshToken.Length == 0
            || !NeedsRefresh(record))
        {
            return state;
        }

        SemaphoreSlim gate = Gates.GetOrAdd(keyHash, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Re-read behind the gate. The request that waited here usually finds the winner's
            // fresh tokens already in place, and never calls the provider at all.
            SessionRecord? current = await store.GetAsync(keyHash, ct).ConfigureAwait(false);
            if (current is null)
            {
                ClearCookie(context);
                return SessionState.Unknown;
            }

            if (!NeedsRefresh(current))
            {
                return new SessionState(SessionRead.Valid, current.Subject, current, keyHash);
            }

            return await RefreshBehindTheGateAsync(context, current, keyHash, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
            {
                Gates.TryRemove(new KeyValuePair<string, SemaphoreSlim>(keyHash, gate));
            }
        }
    }

    private async Task<SessionState> RefreshBehindTheGateAsync(
        HttpContext context,
        SessionRecord current,
        string keyHash,
        CancellationToken ct)
    {
        string refreshToken = _protector.Unprotect(current.ProtectedRefreshToken);
        SignInResult<ProviderTokens> refreshed = await client.RefreshAsync(refreshToken, ct).ConfigureAwait(false);

        if (refreshed.Outcome == SignInOutcome.Refused)
        {
            LogRefreshRefused(logger, current.Subject);
            await store.RemoveAsync(keyHash, ct).ConfigureAwait(false);
            ClearCookie(context);
            return SessionState.Unknown;
        }

        if (refreshed.Outcome == SignInOutcome.Unavailable)
        {
            LogRefreshUnavailable(logger, current.Subject);
            return new SessionState(SessionRead.Valid, current.Subject, current, keyHash);
        }

        ProviderTokens tokens = refreshed.Match(
            static value => value,
            static _ => throw new InvalidOperationException("Refused was handled above."),
            static () => throw new InvalidOperationException("Unavailable was handled above."));

        DateTimeOffset now = clock.GetUtcNow();
        string rotated = tokens.RefreshToken is { Length: > 0 } issued ? issued : refreshToken;
        SessionRecord replacement = current with
        {
            ProtectedAccessToken = _protector.Protect(tokens.AccessToken),
            ProtectedRefreshToken = _protector.Protect(rotated),
            AccessTokenExpiresAt = now.AddSeconds(tokens.ExpiresIn),
            RefreshTokenHash = OpaqueTokens.Hash(rotated),
        };

        SessionReplace outcome = await store
            .ReplaceIfMatchAsync(keyHash, current.RefreshTokenHash, replacement, ct)
            .ConfigureAwait(false);

        switch (outcome)
        {
            case SessionReplace.Replaced:
                return new SessionState(SessionRead.Valid, replacement.Subject, replacement, keyHash);

            case SessionReplace.Conflict:
                // Another instance refreshed while this one was talking to the provider. Its
                // tokens are the live ones; the pair just fetched here is already stale.
                SessionRecord? winner = await store.GetAsync(keyHash, ct).ConfigureAwait(false);
                return winner is null
                    ? SessionState.Unknown
                    : new SessionState(SessionRead.Valid, winner.Subject, winner, keyHash);

            case SessionReplace.Gone:
            default:
                ClearCookie(context);
                return SessionState.Unknown;
        }
    }

    /// <summary>True when the access token is inside the skew of its expiry, or already past it.</summary>
    private bool NeedsRefresh(SessionRecord record) =>
        record.AccessTokenExpiresAt - options.RefreshSkew <= clock.GetUtcNow();

    /// <summary>
    /// Ends the session this request carries: removes the row, then expires the cookie.
    /// </summary>
    /// <remarks>
    /// In that order. Expiring the cookie first leaves a live row addressed by a value the browser
    /// has just been told to forget, which is a session nobody can end.
    /// </remarks>
    /// <param name="context">The request signing out.</param>
    /// <param name="ct">Cancellation token.</param>
    internal async Task RevokeAsync(HttpContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Cookies[options.CookieName] is { Length: > 0 } cookieValue)
        {
            await store.RemoveAsync(OpaqueTokens.Hash(cookieValue), ct).ConfigureAwait(false);
        }

        ClearCookie(context);
    }

    /// <summary>
    /// Deletes the session cookie. The ONE implementation: the authentication handler's sign-out
    /// and the sign-out endpoint both come here, so the two <c>Set-Cookie</c> headers cannot drift
    /// apart (R12).
    /// </summary>
    /// <param name="context">The response to write the deletion onto.</param>
    internal void ClearCookie(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        NoStore(context);
        context.Response.Cookies.Delete(options.CookieName, CookieOptionsFor(null));
    }

    /// <summary>
    /// Adds <c>no-store</c> to whatever the consumer already asked for, rather than replacing it.
    /// </summary>
    /// <remarks>
    /// This was a plain assignment until the first consumer of the sign-out seam was reviewed
    /// under spec 009. justsmtp's dashboard gate sets
    /// <c>no-store, no-cache, must-revalidate, max-age=0</c> before the request reaches the
    /// account lookup that decides to sign out; the assignment left one directive of the four and
    /// an orphaned <c>Pragma: no-cache</c> beside it, on the one response whose cache posture
    /// matters most. A library cannot know that a consumer's header is stricter than its own, so
    /// it must not overwrite one.
    /// <para>
    /// A response that ends a session must not be served from a cache still holding the signed-in
    /// page, which is why it is set here rather than at each call site.
    /// </para>
    /// </remarks>
    /// <param name="context">The response to add the directive to.</param>
    internal static void NoStore(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string existing = context.Response.Headers.CacheControl.ToString();

        if (existing.Length == 0)
        {
            context.Response.Headers.CacheControl = "no-store";
            return;
        }

        foreach (string directive in existing.Split(','))
        {
            if (directive.AsSpan().Trim().Equals("no-store", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        context.Response.Headers.CacheControl = existing + ", no-store";
    }

    /// <summary>
    /// The cookie name this manager reads and writes, so the authentication handler can tell an
    /// anonymous request from one presenting a cookie without holding a second copy of the options.
    /// </summary>
    internal string CookieName => options.CookieName;

    /// <summary>The plaintext access token on a live session, or null when none is kept.</summary>
    /// <param name="record">A record from <see cref="ReadAsync"/>.</param>
    internal string? AccessToken(SessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.ProtectedAccessToken is { Length: > 0 } protectedToken
            ? _protector.Unprotect(protectedToken)
            : null;
    }

    private void WriteCookie(HttpContext context, string value, DateTimeOffset expiresAt) =>
        context.Response.Cookies.Append(options.CookieName, value, CookieOptionsFor(expiresAt));

    /// <summary>
    /// The cookie's attributes, fixed. <c>Path</c> is <c>/</c> and there is no <c>Domain</c>
    /// because the <c>__Host-</c> prefix requires both, and a browser silently ignores a
    /// <c>__Host-</c> cookie that carries either wrong.
    /// </summary>
    private static CookieOptions CookieOptionsFor(DateTimeOffset? expiresAt) =>
        new()
        {
            HttpOnly = SignInOptions.CookieHttpOnly,
            Secure = SignInOptions.CookieSecure,
            SameSite = SignInOptions.CookieSameSite,
            Path = "/",
            IsEssential = true,
            Expires = expiresAt,
        };

    [LoggerMessage(1, LogLevel.Information, "Signed in {Subject}")]
    private static partial void LogSignedIn(ILogger logger, string subject);

    [LoggerMessage(2, LogLevel.Information, "The provider refused to refresh {Subject}; the session has ended")]
    private static partial void LogRefreshRefused(ILogger logger, string subject);

    [LoggerMessage(3, LogLevel.Warning, "Could not reach the provider to refresh {Subject}; keeping the session")]
    private static partial void LogRefreshUnavailable(ILogger logger, string subject);
}

/// <summary>What the session cookie on a request turned out to be worth.</summary>
/// <param name="Read">Valid, expired, or nothing this host knows about.</param>
/// <param name="Subject">The signed-in subject, on <see cref="SessionRead.Valid"/> only.</param>
/// <param name="Record">The stored row, on <see cref="SessionRead.Valid"/> only.</param>
/// <param name="KeyHash">The row's key, for a caller that is about to replace or remove it.</param>
internal readonly record struct SessionState(
    SessionRead Read,
    string? Subject,
    SessionRecord? Record,
    string? KeyHash)
{
    /// <summary>No cookie, or a cookie this host has no row for.</summary>
    internal static SessionState Unknown => new(SessionRead.Unknown, null, null, null);
}

/// <summary>The three things a presented session cookie can be.</summary>
internal enum SessionRead
{
    /// <summary>A live session.</summary>
    Valid,

    /// <summary>A session this host minted, past its lifetime.</summary>
    Expired,

    /// <summary>No cookie, an unknown value, or a value altered in transit. All one answer.</summary>
    Unknown,
}
