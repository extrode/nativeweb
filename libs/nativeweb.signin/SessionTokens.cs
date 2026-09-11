using System.Security.Claims;

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
    /// no row for, an expired session, a provider that issued no token, and a token that has
    /// expired with no refresh token to replace it. The caller's move in all of them is the same —
    /// do not call the provider — so there is nothing a richer return value would let it do
    /// differently. It does NOT cover <see cref="SignInOptions.RetainTokens"/> being off: that is a
    /// wiring mistake rather than a state, and it throws.
    /// <para>
    /// Null is not "sign in again". A signed-in visitor gets null whenever the provider is
    /// unreachable and the token in hand has already expired, and challenging on that redirects
    /// into the provider that is down, which comes back, and asks again. Branch on
    /// <see cref="HttpContext.User"/> to decide whether to challenge, and on this to decide whether
    /// to make the call.
    /// </para>
    /// <para>
    /// It refreshes first when the token is inside <see cref="SignInOptions.RefreshSkew"/>, through
    /// the same gated, compare-and-swapped path the authentication handler uses, so two requests on
    /// one session still call the provider once between them.
    /// </para>
    /// <para>
    /// A refresh the provider REFUSES ends the session: the row goes, the cookie is expired, and
    /// <see cref="HttpContext.User"/> is reset to anonymous for the remainder of THIS request —
    /// without that last part the rest of the request renders as signed in while the cookie says
    /// otherwise, which is the defect
    /// <see cref="SessionAuthenticationHandler.SignOutAsync"/> exists to avoid. Expiring the cookie
    /// writes a header, so a refusal after the response has started throws.
    /// </para>
    /// <para>
    /// Never call this from work that outlives the request. <see cref="HttpContext"/> is pooled and
    /// reset when the response completes, so a captured one presents the NEXT visitor's cookie and
    /// hands back their token. Capture the token, not the context.
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
    /// <exception cref="InvalidOperationException">
    /// This scheme has <see cref="SignInOptions.RetainTokens"/> off and keeps no token to give; or
    /// the provider refused the refresh after the response had started, so the session cookie can
    /// no longer be expired.
    /// </exception>
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

        if (!sessions.RetainsTokens)
        {
            // Not null. A scheme that keeps no tokens answers null for every signed-in visitor
            // forever, and a caller written against the documented null — challenge, sign in,
            // ask again — loops between this product and the provider with nothing to show for
            // it. The library's posture is that a wiring mistake is named, not absorbed.
            throw new InvalidOperationException(
                $"{nameof(GetAccessTokenAsync)} was called on a scheme with "
                + $"{nameof(SignInOptions)}.{nameof(SignInOptions.RetainTokens)} off, which keeps "
                + "no token to give. Turn it on for the scheme whose token you want.");
        }

        SessionState state = await sessions.ReadAsync(context, ct).ConfigureAwait(false);

        if (state.Read != SessionRead.Valid)
        {
            return null;
        }

        SessionState fresh = await sessions.EnsureFreshAsync(context, state, ct).ConfigureAwait(false);

        if (fresh.Read != SessionRead.Valid)
        {
            // The refresh ended the session. EnsureFreshAsync has removed the row and expired the
            // cookie; the cookie governs the NEXT request and this governs the rest of THIS one.
            // The authentication handler has already run by the time a product calls this, so
            // without the reset the page renders signed in for a visitor who no longer is.
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            return null;
        }

        // The record from the REFRESHED state, not the one ReadAsync returned: a refresh rotates
        // the pair and writes a new row, and the original record still holds the token the
        // provider has just been told to forget.
        return fresh.Record is { } record ? sessions.AccessToken(record) : null;
    }
}
