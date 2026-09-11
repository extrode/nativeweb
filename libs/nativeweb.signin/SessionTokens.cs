using Microsoft.AspNetCore.Http;

namespace NativeWeb.SignIn;

/// <summary>
/// Hands a product the access token the session is holding, refreshed if it was due.
/// </summary>
/// <remarks>
/// The reason this exists: with <see cref="SignInOptions.RetainTokens"/> on, the library keeps the
/// provider's tokens so it can refresh them, and until now nothing could read one. A product
/// calling the provider's own API on the visitor's behalf — the case the retention is FOR — had
/// either to obtain a second token of its own or to reach into the store and decrypt the row
/// itself, and a second rotation path against a provider that revokes a reused refresh token is
/// how a whole token family dies.
/// <para>
/// It is read-only by construction. Nothing here mints, replaces or revokes anything, which is the
/// same line <see cref="SessionManager"/> draws around its own public surface.
/// </para>
/// </remarks>
public interface ISessionTokens
{
    /// <summary>
    /// The access token for the session on <paramref name="context"/>, or <see langword="null"/>
    /// when there is none to give.
    /// </summary>
    /// <remarks>
    /// Null covers every case a caller has the same answer for: no cookie, a cookie this host has
    /// no row for, an expired session, <see cref="SignInOptions.RetainTokens"/> off, a provider
    /// that issued no token, and a token that has expired with no refresh token to replace it. The
    /// caller's move in all of them is the same — do not call the provider — so there is nothing a
    /// richer return value would let it do differently.
    /// <para>
    /// It refreshes first when the token is inside <see cref="SignInOptions.RefreshSkew"/>, through
    /// the same gated, compare-and-swapped path the authentication handler uses, so two requests on
    /// one session still call the provider once between them.
    /// </para>
    /// <para>
    /// A refresh the provider REFUSES ends the session and expires the cookie, which writes a
    /// header. Call this before the response has started, as you would anything that can sign a
    /// visitor out.
    /// </para>
    /// <para>
    /// Each call reads the session store. Ask once and pass the token down rather than calling it
    /// per outbound request: the result is deliberately not cached on the context, because a
    /// plaintext token parked in <see cref="HttpContext.Items"/> is readable by every component
    /// that runs after it.
    /// </para>
    /// </remarks>
    /// <param name="context">The request being served.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The plaintext access token, or <see langword="null"/>. Secret; never logged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken ct = default);
}

/// <summary>
/// The one implementation, over one scheme's <see cref="SessionManager"/>.
/// </summary>
/// <remarks>
/// Registered keyed on the scheme name like everything else the scheme owns, and additionally
/// unkeyed for the FIRST scheme registered — the same scheme <c>AddSignIn</c> makes the host's
/// authentication default. A product with one provider injects <see cref="ISessionTokens"/> and
/// names nothing; a product with two resolves the one it means by key, as it already must for its
/// options and its store.
/// </remarks>
/// <param name="sessions">The scheme's session manager.</param>
internal sealed class SessionTokens(SessionManager sessions) : ISessionTokens
{
    /// <inheritdoc/>
    public async Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        SessionState state = await sessions.ReadAsync(context, ct).ConfigureAwait(false);

        if (state.Read != SessionRead.Valid)
        {
            return null;
        }

        SessionState fresh = await sessions.EnsureFreshAsync(context, state, ct).ConfigureAwait(false);

        // Re-read the record from the refreshed state rather than the one ReadAsync returned. The
        // refresh rotates the pair and writes a new row; the original record still holds the token
        // the provider has just been told to forget.
        return fresh.Read == SessionRead.Valid && fresh.Record is { } record
            ? sessions.AccessToken(record)
            : null;
    }
}
