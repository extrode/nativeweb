using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NativeWeb.SignIn;

/// <summary>
/// Turns the opaque session cookie into a <see cref="ClaimsPrincipal"/>, and ends the session when
/// a consumer calls <c>SignOutAsync</c>.
/// </summary>
/// <remarks>
/// The only claim is <see cref="ClaimTypes.NameIdentifier"/>, carrying the provider's subject.
/// Nothing else is carried, because anything else would be a copy of a row frozen at sign-in time,
/// and a customer who changes something about their account should not have to sign out to see it.
/// <para>
/// A cookie this host does not know yields <see cref="AuthenticateResult.NoResult"/>, never
/// <see cref="AuthenticateResult.Fail(string)"/> and never a throw. An unknown cookie is what an
/// attacker sends, and turning it into an exception hands them an error page and a log flood; a
/// request carrying one is an anonymous request.
/// </para>
/// <para>
/// The cookie value is opaque, so a modified cookie is indistinguishable from one that was never
/// issued — there is no "bad signature" case to report, which is why the reasons this handler can
/// log are the two <see cref="SessionRead"/> values that are not <see cref="SessionRead.Valid"/>.
/// </para>
/// <para>
/// It also implements <see cref="IAuthenticationSignOutHandler"/>, which is what makes
/// <c>HttpContext.SignOutAsync(SchemeName)</c> available anywhere in a request (R12). Without it
/// the only sanctioned sign-out is a form post to the logout path, and a consumer that discovers
/// mid-request that an account is disabled cannot post a form — so it hand-copies the cookie
/// attributes instead, and a delete whose attributes drift from these does nothing while the page
/// says the user is signed out.
/// </para>
/// </remarks>
/// <param name="options">Scheme options, supplied by the authentication stack.</param>
/// <param name="loggerFactory">Logger factory, supplied by the authentication stack.</param>
/// <param name="encoder">URL encoder, supplied by the authentication stack.</param>
/// <param name="services">
/// The container, for the <see cref="SessionManager"/> registered under THIS handler's scheme name.
/// Resolved by key rather than injected, because a host can register more than one provider and each
/// scheme owns its own manager, options, stores and cookie name — an injected manager would be
/// whichever one was registered last, and every scheme would then read the same sessions.
/// </param>
public sealed partial class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IServiceProvider services)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder),
      IAuthenticationSignOutHandler
{
    /// <summary>The scheme name a host registers this handler under when it names no other.</summary>
    public const string SchemeName = "NativeWebSignIn";

    private SessionManager Sessions => services.GetRequiredKeyedService<SessionManager>(Scheme.Name);

    /// <inheritdoc/>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        SessionState state = await Sessions.ReadAsync(Context, Context.RequestAborted)
            .ConfigureAwait(false);

        if (state.Read != SessionRead.Valid)
        {
            return Anonymous(state.Read);
        }

        SessionState fresh = await Sessions
            .EnsureFreshAsync(Context, state, Context.RequestAborted)
            .ConfigureAwait(false);

        if (fresh.Read != SessionRead.Valid)
        {
            // The refresh ended the session: the provider refused the refresh token, or the row
            // went away under us. EnsureFreshAsync has already expired the cookie.
            return Anonymous(fresh.Read);
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, fresh.Subject!)],
            Scheme.Name);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    /// <summary>
    /// Ends the session this request carries. Reached by <c>HttpContext.SignOutAsync(SchemeName)</c>
    /// from anywhere in a request — a filter, a middleware, an endpoint.
    /// </summary>
    /// <param name="properties">Ignored. There is no redirect to negotiate and no state to carry:
    /// ending the session is removing the row and expiring the cookie. Where the consumer goes next
    /// is the consumer's decision, made at its own call site, and a <c>RedirectUri</c> honoured here
    /// would compete with it.</param>
    /// <remarks>
    /// Calls the SAME <see cref="SessionManager.RevokeAsync"/> the sign-out endpoint calls, rather
    /// than a second deletion that agrees with it today (R12). Two cookie deletions in one library
    /// is one more than can be kept in step.
    /// <para>
    /// Also resets <see cref="HttpContext.User"/> to an anonymous principal. Without it this request
    /// stays authenticated after its own sign-out — measured during a spec 009 review: a filter that
    /// discovered the account was disabled deleted the cookie and then rendered the signed-in page
    /// anyway, which is the case the seam exists for. The cookie governs the NEXT request;
    /// <c>Context.User</c> governs this one, and both are the session.
    /// </para>
    /// <para>
    /// THROWS if the response has already started, because expiring a cookie writes a header.
    /// Middleware that wraps the pipeline must sign out before calling the next component.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The response has already started, so the session
    /// cookie can no longer be expired.</exception>
    public async Task SignOutAsync(AuthenticationProperties? properties)
    {
        await Sessions.RevokeAsync(Context, Context.RequestAborted).ConfigureAwait(false);
        Context.User = new ClaimsPrincipal(new ClaimsIdentity());
    }

    /// <summary>
    /// No principal, and the presented cookie expired on the way out.
    /// </summary>
    /// <remarks>
    /// Expiring it matters: without it a rejected cookie is re-presented on every request for the
    /// remainder of its fortnight, and each one is a log line and a store lookup. The attributes
    /// come from the same place the set did, or the browser keeps it.
    /// </remarks>
    private AuthenticateResult Anonymous(SessionRead read)
    {
        if (!Request.Cookies.ContainsKey(Sessions.CookieName))
        {
            // No cookie is not a rejection: it is an anonymous request, and a public site is full
            // of them. Nothing to expire and nothing to log.
            return AuthenticateResult.NoResult();
        }

        LogRejected(Logger, read.ToString());
        Sessions.ClearCookie(Context);
        return AuthenticateResult.NoResult();
    }

    [LoggerMessage(1, LogLevel.Debug, "A session cookie was not accepted ({Reason}).")]
    private static partial void LogRejected(ILogger logger, string reason);
}
