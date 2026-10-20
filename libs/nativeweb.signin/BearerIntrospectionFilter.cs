using System.Collections.Concurrent;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

using NativeWeb.Security;

namespace NativeWeb.SignIn;

/// <summary>
/// Checks the <c>Authorization: Bearer</c> token on an API endpoint by asking the provider's
/// introspection endpoint, with a short cache so a busy client does not put one provider call on
/// every request.
/// </summary>
/// <remarks>
/// An endpoint filter rather than an authentication scheme, because the two mechanisms answer
/// different questions and a product usually wants both: the browser half of the product signs in
/// through <see cref="SessionAuthenticationHandler"/>, and a machine client presents a token to a
/// handful of API routes. Mounting introspection as a second scheme would make every browser request
/// carry a scheme selector it has no use for.
/// <para>
/// EVERY refusal is the same response — same status, same body, same headers — whether the token is
/// unknown, revoked, expired, malformed or absent (R16). A refusal that varies is an oracle: a
/// caller holding a stolen token learns from the difference whether it was ever real, and a caller
/// probing with guesses learns when one lands.
/// </para>
/// <para>
/// A provider that cannot be REACHED is a different answer and gets a different status. Nothing was
/// decided about the token, and answering 401 would tell a legitimate client to discard a token that
/// is probably fine.
/// </para>
/// </remarks>
/// <param name="client">The provider client.</param>
/// <param name="cache">The shared introspection cache.</param>
/// <param name="options">Validated options.</param>
/// <param name="clock">The clock the TTL and <c>exp</c> are judged against.</param>
/// <param name="logger">This library's logger.</param>
public sealed partial class BearerIntrospectionFilter(
    OAuthClient client,
    IntrospectionCache cache,
    SignInOptions options,
    TimeProvider clock,
    ILogger<BearerIntrospectionFilter> logger)
    : IEndpointFilter
{
    /// <inheritdoc/>
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpContext http = context.HttpContext;

        if (Bearer(http) is not { Length: > 0 } token)
        {
            return Refused(http);
        }

        string key = OpaqueTokens.Hash(token);
        DateTimeOffset now = clock.GetUtcNow();

        if (cache.TryGet(key, now) is { } cached)
        {
            return cached.Active ? await Accept(context, next, cached.Subject).ConfigureAwait(false)
                : Refused(http);
        }

        SignInResult<TokenIntrospection> asked = await client
            .IntrospectAsync(token, http.RequestAborted).ConfigureAwait(false);

        if (asked.Outcome == SignInOutcome.Unavailable)
        {
            // Nothing was decided. A cached verdict is not written, because there is none, and the
            // client is told to come back rather than to throw its token away.
            LogUnavailable(logger);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        TokenIntrospection? verdict = asked.Match<TokenIntrospection?>(
            static value => value,
            static _ => null,
            static () => null);

        // A REFUSED introspection is a verdict: the provider answered and would not vouch for the
        // token. It caches like any other inactive answer, or a caller with a bad token puts a
        // provider call on every one of its retries.
        bool active = verdict is { Active: true };
        string? subject = active ? verdict!.Subject : null;

        cache.Set(key, new IntrospectionVerdict(active, subject), Until(now, verdict));

        return active
            ? await Accept(context, next, subject).ConfigureAwait(false)
            : Refused(http);
    }

    /// <summary>
    /// When a cached verdict stops being usable: the configured TTL, or the token's own
    /// <c>exp</c>, whichever comes first.
    /// </summary>
    /// <remarks>
    /// The ceiling is what stops a 30-second cache from serving a token that expires in 5 seconds
    /// for another 25. A provider that reports no <c>exp</c> gets the TTL alone, which is the most
    /// this filter can honestly claim to know.
    /// </remarks>
    private DateTimeOffset Until(DateTimeOffset now, TokenIntrospection? verdict)
    {
        DateTimeOffset ttl = now + options.IntrospectionCacheLifetime;

        return verdict?.ExpiresAt is { } exp && exp < ttl ? exp : ttl;
    }

    private static async ValueTask<object?> Accept(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next, string? subject)
    {
        if (subject is { Length: > 0 })
        {
            // The endpoint behind this filter reads User the same way a browser endpoint does, so a
            // handler does not have to know which of the two mechanisms let the caller in.
            context.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, subject)],
                    SessionAuthenticationHandler.SchemeName));
        }

        return await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// The one refusal. Constant in every respect, so unknown, revoked, expired, malformed and
    /// absent are indistinguishable from the outside.
    /// </summary>
    private static IResult Refused(HttpContext http)
    {
        // RFC 6750 §3: a 401 carries a challenge. It carries no error code, because "invalid_token"
        // beside "the header was missing" is the difference this method exists to hide.
        http.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
        return Results.StatusCode(StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// The token out of <c>Authorization: Bearer &lt;token&gt;</c>, or null.
    /// </summary>
    /// <remarks>
    /// The scheme is compared case-insensitively per RFC 7235 §2.1, and anything past a single
    /// space is the token. A header with no space, a different scheme, or more than one value is
    /// not a bearer request.
    /// </remarks>
    private static string? Bearer(HttpContext http)
    {
        if (http.Request.Headers.Authorization.Count != 1)
        {
            return null;
        }

        string header = http.Request.Headers.Authorization.ToString();
        const string scheme = "Bearer ";

        return header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            ? header[scheme.Length..].Trim()
            : null;
    }

    [LoggerMessage(1, LogLevel.Warning, "The provider could not be reached to introspect a bearer token.")]
    private static partial void LogUnavailable(ILogger logger);
}

/// <summary>What introspection concluded about a token, as it is held in the cache.</summary>
/// <param name="Active">Whether the provider vouched for the token.</param>
/// <param name="Subject">Who it belongs to, on an active token only.</param>
public readonly record struct IntrospectionVerdict(bool Active, string? Subject);

/// <summary>
/// A short-lived cache of introspection verdicts, keyed by the hash of the token.
/// </summary>
/// <remarks>
/// Keyed by <see cref="OpaqueTokens.Hash"/> rather than by the token, so a memory dump of a running
/// process does not hand over a set of live bearer tokens. The cost of the hash is a rounding error
/// beside the network call it avoids.
/// <para>
/// Registered as a singleton and shared across every endpoint carrying the filter: two API routes
/// checking the same token within the TTL make one provider call between them, not one each.
/// </para>
/// </remarks>
public sealed class IntrospectionCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>The verdict held for <paramref name="key"/>, or null when there is none to use.</summary>
    /// <param name="key">The hash of the token.</param>
    /// <param name="now">The current instant.</param>
    public IntrospectionVerdict? TryGet(string key, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_entries.TryGetValue(key, out Entry entry))
        {
            return null;
        }

        if (entry.Until <= now)
        {
            // Judged on read, not on a timer. An entry nothing asks for again costs one dictionary
            // slot; an entry served past its instant is a revoked token still working.
            _entries.TryRemove(key, out _);
            return null;
        }

        return entry.Verdict;
    }

    /// <summary>Holds a verdict until <paramref name="until"/>.</summary>
    /// <param name="key">The hash of the token.</param>
    /// <param name="verdict">What introspection concluded.</param>
    /// <param name="until">When the verdict stops being usable.</param>
    /// <remarks>
    /// An <paramref name="until"/> that has already passed is written and then never served, which
    /// is how a TTL of zero disables the cache without a second code path to keep in step with this
    /// one: the first read after it drops it and asks the provider again.
    /// </remarks>
    public void Set(string key, IntrospectionVerdict verdict, DateTimeOffset until)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _entries[key] = new Entry(verdict, until);
    }

    /// <summary>Drops entries past their instant. Garbage collection; nothing depends on it running.</summary>
    /// <param name="now">The current instant.</param>
    public void Sweep(DateTimeOffset now)
    {
        foreach (KeyValuePair<string, Entry> entry in _entries)
        {
            if (entry.Value.Until <= now)
            {
                _entries.TryRemove(entry.Key, out _);
            }
        }
    }

    /// <summary>How many verdicts are held, swept or not. For tests and for a health page.</summary>
    public int Count => _entries.Count;

    private readonly record struct Entry(IntrospectionVerdict Verdict, DateTimeOffset Until);
}
