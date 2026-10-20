using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using NativeWeb.Security;

namespace NativeWeb.SignIn.Testing;

/// <summary>
/// Mints sessions for a consumer's integration tests, through the framework's own minting code.
/// </summary>
/// <remarks>
/// The point of this type is that it does NOT reimplement the session format (R20). A test that
/// hand-rolls a cookie value and a store row keeps passing after the format changes, and then
/// stops testing anything; one that goes through <see cref="SessionManager"/> fails to compile
/// instead. The <c>InternalsVisibleTo</c> grant that makes this possible is scoped to this
/// assembly alone, and this assembly is a <c>DevelopmentDependency</c>, so nothing in a consumer's
/// production closure can reach a minting API.
/// <para>
/// <b>Minting is not public on the library on purpose.</b> A public mint taking a subject is an
/// authentication bypass any caller can invoke with any identity it likes.
/// </para>
/// </remarks>
public static class TestSession
{
    /// <summary>
    /// Mints a live session and returns its cookie value.
    /// </summary>
    /// <param name="services">The host's services. A scope is created internally.</param>
    /// <param name="subject">Who the session is for.</param>
    /// <param name="scheme">The authentication scheme to mint against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cookie value to present. Pair it with <see cref="CookieHeader"/>.</returns>
    public static async Task<string> MintAsync(
        IServiceProvider services,
        string subject,
        string scheme = SessionAuthenticationHandler.SchemeName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        using IServiceScope scope = services.CreateScope();
        SessionManager sessions = scope.ServiceProvider
            .GetRequiredKeyedService<SessionManager>(scheme);

        return await sessions
            .MintAsync(new DefaultHttpContext(), new ProviderUser(subject, null, null), null, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a session row that is already past its expiry, and returns its cookie value.
    /// </summary>
    /// <remarks>
    /// Written through the store rather than through <see cref="SessionManager"/>, because minting
    /// reads the clock and a valid mint cannot produce a past expiry. What a reader does with the
    /// row is still the library's own judgement: expiry is enforced where a session is READ, so
    /// this is a real expired session and not a simulation of one.
    /// </remarks>
    /// <param name="services">The host's services.</param>
    /// <param name="subject">Who the session was for.</param>
    /// <param name="expiredFor">How long ago it lapsed. Must be positive.</param>
    /// <param name="scheme">The authentication scheme to mint against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cookie value of a session no reader will accept.</returns>
    public static async Task<string> MintExpiredAsync(
        IServiceProvider services,
        string subject,
        TimeSpan? expiredFor = null,
        string scheme = SessionAuthenticationHandler.SchemeName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        TimeSpan lapsed = expiredFor ?? TimeSpan.FromMinutes(1);

        if (lapsed <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiredFor), lapsed, "An expired session must have lapsed some time ago.");
        }

        using IServiceScope scope = services.CreateScope();
        ISessionStore store = scope.ServiceProvider.GetRequiredKeyedService<ISessionStore>(scheme);
        TimeProvider clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        DateTimeOffset now = clock.GetUtcNow();
        string cookieValue = OpaqueTokens.Generate();

        await store.CreateAsync(
            OpaqueTokens.Hash(cookieValue),
            new SessionRecord(
                Subject: subject,
                IssuedAt: now - lapsed - TimeSpan.FromMinutes(1),
                ExpiresAt: now - lapsed,
                ProtectedAccessToken: "",
                ProtectedRefreshToken: "",
                AccessTokenExpiresAt: now - lapsed,
                RefreshTokenHash: ""),
            ct).ConfigureAwait(false);

        return cookieValue;
    }

    /// <summary>
    /// Alters a cookie value, so it addresses no row this host issued.
    /// </summary>
    /// <remarks>
    /// <b>There is no signature to forge here, and that is the design.</b> The cookie is an opaque
    /// random string whose only meaning is the row it keys, so "tampered" and "made up" are the
    /// same input to a reader, and both are refused as unknown. A test asserting a distinct
    /// "tampered" outcome would be asserting a state the library does not have (R21).
    /// </remarks>
    /// <param name="cookieValue">A cookie value from one of the mint methods.</param>
    /// <returns>A value of the same shape that no store will match.</returns>
    public static string Tamper(string cookieValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookieValue);

        char first = cookieValue[0];

        return (first == 'A' ? 'B' : 'A') + cookieValue[1..];
    }

    /// <summary>
    /// Builds the <c>Cookie</c> request header carrying a minted session.
    /// </summary>
    /// <param name="services">The host's services, for the configured cookie name.</param>
    /// <param name="cookieValue">The value returned by one of the mint methods.</param>
    /// <param name="scheme">The authentication scheme whose cookie name to use.</param>
    /// <returns>A header value for <c>HttpRequestMessage.Headers.Add("Cookie", …)</c>.</returns>
    public static string CookieHeader(
        IServiceProvider services,
        string cookieValue,
        string scheme = SessionAuthenticationHandler.SchemeName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(cookieValue);

        SignInOptions options = services.GetRequiredKeyedService<SignInOptions>(scheme);

        return $"{options.CookieName}={cookieValue}";
    }
}
