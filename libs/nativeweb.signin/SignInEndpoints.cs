using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NativeWeb.SignIn;

/// <summary>
/// The three endpoints the library owns: begin the authorization-code flow, take the provider's
/// callback, sign out. The consumer chooses all three paths (R11).
/// </summary>
/// <remarks>
/// There is no page rendered anywhere here. The sign-in page, if a product has one at all, is the
/// consumer's markup; failures arrive at it as a redirect carrying a machine code from
/// <see cref="SignInFailureCode"/>, which the consumer maps to its own words.
/// <para>
/// Every <c>Map*</c> call below is written out literally with an explicitly typed lambda so the
/// Request Delegate Generator can see it and emit a compile-time delegate. A route built in a loop,
/// or handed a <see cref="Delegate"/> variable, falls back to the reflection-based overload and
/// fails the NativeAOT publish gate with IL2026/IL3050 — in the CONSUMER'S publish, not here.
/// </para>
/// </remarks>
public static partial class SignInEndpoints
{
    /// <summary>
    /// Maps the sign-in, callback and sign-out endpoints at the configured paths.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns><paramref name="endpoints"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><c>AddSignIn</c> was not called.</exception>
    public static IEndpointRouteBuilder MapSignIn(
        this IEndpointRouteBuilder endpoints,
        string scheme = SessionAuthenticationHandler.SchemeName)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        SignInOptions options = endpoints.ServiceProvider
            .GetRequiredKeyedService<SignInOptions>(scheme);

        // Every service below is resolved from the REQUEST's provider under this scheme's key,
        // rather than taken as a lambda parameter. A keyed parameter needs its key as a
        // compile-time attribute argument and the scheme is chosen at runtime, so the alternative
        // is one set of unkeyed registrations — which is one provider per host, and a second
        // AddSignIn silently overwriting the first.
        endpoints.MapGet(options.LoginPath, (
            HttpContext context, TimeProvider time, ILoggerFactory loggers) =>
            GetLoginAsync(
                context,
                context.RequestServices.GetRequiredKeyedService<OAuthClient>(scheme),
                context.RequestServices.GetRequiredKeyedService<ILoginFlowStore>(scheme),
                context.RequestServices.GetRequiredKeyedService<SignInOptions>(scheme),
                time,
                loggers,
                context.RequestServices.GetKeyedService<SignInPolicy>(scheme)));

        endpoints.MapGet(options.CallbackPath, (
            HttpContext context, ILoggerFactory loggers) =>
            GetCallbackAsync(
                context,
                context.RequestServices.GetRequiredKeyedService<OAuthClient>(scheme),
                context.RequestServices.GetRequiredKeyedService<ILoginFlowStore>(scheme),
                context.RequestServices.GetRequiredKeyedService<SessionManager>(scheme),
                context.RequestServices.GetRequiredKeyedService<SignInOptions>(scheme),
                loggers,
                context.RequestServices.GetKeyedService<SignInResolver>(scheme)));

        endpoints.MapPost(options.LogoutPath, (
            HttpContext context, IAntiforgery antiforgery, ILoggerFactory loggers) =>
            PostLogoutAsync(
                context,
                antiforgery,
                context.RequestServices.GetRequiredKeyedService<SessionManager>(scheme),
                context.RequestServices.GetRequiredKeyedService<SignInOptions>(scheme),
                loggers));

        return endpoints;
    }

    /// <summary>
    /// Requires an active bearer token on this endpoint or group, checked by introspection.
    /// </summary>
    /// <typeparam name="TBuilder">The route or group builder.</typeparam>
    /// <param name="builder">What to protect.</param>
    /// <param name="scheme">The authentication scheme whose provider to introspect against.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="scheme"/> is blank.</exception>
    /// <remarks>
    /// This exists because <c>AddEndpointFilter&lt;BearerIntrospectionFilter&gt;()</c> CANNOT work
    /// here. That overload activates the filter from the container by type, and every service the
    /// filter needs is registered keyed on the scheme name, so the activation fails at the first
    /// request with a message naming <c>OAuthClient</c> rather than the missing scheme. Resolving
    /// per request out of <c>RequestServices</c> is what makes a keyed provider reachable from a
    /// filter at all.
    /// </remarks>
    public static TBuilder RequireBearerToken<TBuilder>(
        this TBuilder builder,
        string scheme = SessionAuthenticationHandler.SchemeName)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        builder.AddEndpointFilter(async (context, next) =>
        {
            IServiceProvider services = context.HttpContext.RequestServices;

            var filter = new BearerIntrospectionFilter(
                services.GetRequiredKeyedService<OAuthClient>(scheme),
                services.GetRequiredKeyedService<IntrospectionCache>(scheme),
                services.GetRequiredKeyedService<SignInOptions>(scheme),
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<ILogger<BearerIntrospectionFilter>>());

            return await filter.InvokeAsync(context, next).ConfigureAwait(false);
        });

        return builder;
    }

    /// <summary>
    /// Begins the flow: mint <c>state</c> and a PKCE verifier, record them, and redirect the
    /// browser to the provider's authorization endpoint.
    /// </summary>
    /// <remarks>
    /// A <c>GET</c>, not a <c>POST</c>, and therefore carrying no antiforgery token. That is not an
    /// oversight: this endpoint takes no action on the user's behalf and reads no session. The worst
    /// a forced navigation achieves is a login prompt the user can decline, and requiring a token
    /// here would break the ordinary case of a link to <c>/signin</c> in a consumer's own markup.
    /// The state parameter is what defends the CALLBACK, which is the request that acts.
    /// </remarks>
    internal static async Task<IResult> GetLoginAsync(
        HttpContext context,
        OAuthClient client,
        ILoginFlowStore flows,
        SignInOptions options,
        TimeProvider time,
        ILoggerFactory loggers,
        SignInPolicy? policy = null)
    {
        SessionManager.NoStore(context);

        if (!client.IsConfigured)
        {
            // Degrade, do not throw. An all-blank configuration boots (R19) with sign-in
            // unavailable, so the one thing this must not do is answer a public GET with a 500.
            LogNotConfigured(loggers.CreateLogger(typeof(SignInEndpoints)));
            return Results.Redirect(Failure(options, SignInFailureCode.ProviderUnavailable));
        }

        // The consumer's own judgement, if it registered one (R22). BEFORE the flow is recorded and
        // before the browser is sent anywhere, which is the point: a refusal here costs no provider
        // round trip and leaves no row for a callback to take.
        if (policy is not null && !await Allowed(context, policy, loggers).ConfigureAwait(false))
        {
            // The SAME code every other refusal gets (R23). A refusal that could vary is a refusal
            // that can be used to tell one caller from another — which is why SignInPolicy returns
            // a boolean and has no way to name a code.
            return Results.Redirect(Failure(options, SignInFailureCode.Refused));
        }

        string returnTo = ReturnPath.Sanitise(
            context.Request.Query[SignInQuery.ReturnTo].ToString(), options.DefaultReturnPath);

        string state = OAuthClient.NewState();
        string verifier = OAuthClient.NewVerifier();

        // Recorded BEFORE the browser is sent anywhere, because the callback can only be taken
        // against a row that exists. The redirect is the last thing that happens.
        //
        // The expiry is computed HERE, from the configured lifetime and the application's clock,
        // rather than being left to the store. One lifetime then governs every implementation, and
        // a store never has to reach into this library's options to discover what it is.
        DateTimeOffset expiresAt = time.GetUtcNow() + options.FlowLifetime;

        await flows.StartAsync(
            state,
            verifier,
            returnTo,
            expiresAt,
            context.RequestAborted).ConfigureAwait(false);

        // The state goes to the browser as well as to the store. Without the browser's copy the
        // callback trusts a query parameter anyone can supply; see SessionManager.WriteFlowCookie.
        SessionManager.WriteFlowCookie(context, options, state, expiresAt);

        return Results.Redirect(AuthorizationUrl(options, state, OAuthClient.Challenge(verifier)));
    }

    /// <summary>
    /// Takes the provider's callback: consume the flow, exchange the code, ask who signed in, and
    /// mint a session.
    /// </summary>
    internal static async Task<IResult> GetCallbackAsync(
        HttpContext context,
        OAuthClient client,
        ILoginFlowStore flows,
        SessionManager sessions,
        SignInOptions options,
        ILoggerFactory loggers,
        SignInResolver? resolver = null)
    {
        SessionManager.NoStore(context);

        ILogger logger = loggers.CreateLogger(typeof(SignInEndpoints));

        string code = context.Request.Query[SignInQuery.Code].ToString();
        string state = context.Request.Query[SignInQuery.State].ToString();

        // A provider-initiated refusal arrives as ?error=... with no code — the user declined at
        // the provider, or the provider declined for them. That is a decision, not a broken
        // callback, and reporting it as an incomplete callback would send the customer looking for
        // a fault that is not there. The provider's own error string is NOT read: no upstream prose
        // reaches this response.
        if (code.Length == 0 && context.Request.Query.ContainsKey(SignInQuery.Error))
        {
            return Results.Redirect(Failure(options, SignInFailureCode.SignInFailed));
        }

        // Cleared on every exit below, taken or refused: one abandoned attempt must not leave a
        // correlator lying about for the next callback to match against.
        string expected = context.Request.Cookies[SessionManager.FlowCookieName(options)] ?? "";
        SessionManager.ClearFlowCookie(context, options);

        if (code.Length == 0 || state.Length == 0)
        {
            return Results.Redirect(Failure(options, SignInFailureCode.CallbackIncomplete));
        }

        // The browser that finishes the flow must be the browser that started it. A callback
        // carrying somebody else's state - the whole shape of login CSRF - has no matching cookie
        // here and never reaches the token endpoint.
        if (expected.Length == 0
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(state)))
        {
            LogFlowCookieMismatch(logger);
            return Results.Redirect(Failure(options, SignInFailureCode.StateMismatch));
        }

        // Bounded before either value reaches the consumer's store or the provider. A query string
        // is smaller than a form body, but "smaller" is not a bound, and neither field has any
        // legitimate reason to be long: state is minted here at 43 characters.
        if (code.Length > MaxCallbackFieldLength || state.Length > MaxCallbackFieldLength)
        {
            return Results.Redirect(Failure(options, SignInFailureCode.CallbackIncomplete));
        }

        FlowTakeResult taken = await flows.TakeAsync(state, context.RequestAborted).ConfigureAwait(false);

        // Three distinguishable codes, not one: the customer's next move differs for each.
        switch (taken.Outcome)
        {
            case FlowTake.Taken:
                break;

            case FlowTake.AlreadyUsed:
                return Results.Redirect(Failure(options, SignInFailureCode.StateUsed));

            case FlowTake.Expired:
                return Results.Redirect(Failure(options, SignInFailureCode.StateExpired));

            default:
                return Results.Redirect(Failure(options, SignInFailureCode.StateUnknown));
        }

        // FlowTakeResult.Taken(default) compiles, so a store CAN report success while carrying
        // nothing usable. Every other branch of this method redirects; an unguarded dereference
        // here would be the one that answers an unauthenticated GET with a 500 and a stack trace.
        if (taken.Flow is not { } flow || flow.Verifier is null || flow.ReturnTo is null)
        {
            LogStoreReturnedNoFlow(logger);
            return Results.Redirect(Failure(options, SignInFailureCode.StateUnknown));
        }

        SignInResult<ProviderTokens> exchanged = await client
            .ExchangeCodeAsync(code, flow.Verifier, context.RequestAborted).ConfigureAwait(false);

        ProviderTokens? tokens = exchanged.Match<ProviderTokens?>(
            static value => value,
            static _ => null,
            static () => null);

        if (tokens is null)
        {
            LogExchangeFailed(logger, exchanged.Outcome.ToString());
            return Results.Redirect(Failure(options, CodeFor(exchanged.Outcome)));
        }

        SignInResult<ProviderUser> who = await client
            .GetUserInfoAsync(tokens.AccessToken, context.RequestAborted).ConfigureAwait(false);

        ProviderUser? user = who.Match<ProviderUser?>(
            static value => value,
            static _ => null,
            static () => null);

        if (user is null)
        {
            LogUserInfoFailed(logger, who.Outcome.ToString());
            return Results.Redirect(Failure(options, CodeFor(who.Outcome)));
        }

        // The consumer's own mapping from a verified provider subject to whatever it calls a user,
        // if it registered one. Without one the provider's subject IS the session subject, which is
        // what makes the seam optional (R24).
        ProviderUser subject = user;

        if (resolver is not null)
        {
            string? local = await resolver(
                context,
                new VerifiedSubject(user.Subject),
                new UnverifiedContactInfo(user.Email),
                context.RequestAborted).ConfigureAwait(false);

            if (local is not { Length: > 0 })
            {
                // The consumer refused locally. No session is minted — a resolver returning null
                // must not produce a signed-in browser.
                return Results.Redirect(Failure(options, SignInFailureCode.Refused));
            }

            subject = user with { Subject = local };
        }

        await sessions.MintAsync(context, subject, tokens, context.RequestAborted).ConfigureAwait(false);

        // Sanitised AGAIN, on the way out. It was sanitised before it was stored, but the store
        // between those two points is the CONSUMER'S — a table anyone with write access to the
        // database can edit, and a seam whose implementation this library never sees. Trusting it
        // here would make an open redirect out of a row update.
        string destination = ReturnPath.Sanitise(flow.ReturnTo, options.DefaultReturnPath);

        // Redirect rather than render: this URL carries a spent code and a spent state, and leaving
        // it in the address bar means it is what gets bookmarked and reloaded.
        return Results.Redirect(destination);
    }

    /// <summary>Ends the session and sends the browser to the configured landing path.</summary>
    internal static async Task<IResult> PostLogoutAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        SessionManager sessions,
        SignInOptions options,
        ILoggerFactory loggers)
    {
        if (!await ValidatedAsync(context, antiforgery, loggers).ConfigureAwait(false))
        {
            // Before any state changes. A cross-site post must not end a session, so the
            // antiforgery check comes ahead of the removal and ahead of the cookie.
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        // RevokeAsync sets no-store itself, through the one implementation the authentication
        // handler's SignOutAsync also calls (R12).
        await sessions.RevokeAsync(context, context.RequestAborted).ConfigureAwait(false);
        return Results.Redirect(options.SignedOutPath);
    }

    /// <summary>
    /// The provider's authorization URL for a flow.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than with <c>QueryHelpers</c> so every parameter is visible at the one
    /// place a reviewer looks for them, and every value is escaped on the way in. <c>scope</c> is
    /// space-delimited per RFC 6749 §3.3 and escaped as a whole, so the delimiter arrives as
    /// <c>%20</c> rather than as a parameter break.
    /// </remarks>
    private static string AuthorizationUrl(SignInOptions options, string state, string challenge) =>
        options.AuthorizationEndpoint
        + (options.AuthorizationEndpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?")
        + "response_type=code"
        + "&client_id=" + Uri.EscapeDataString(options.ClientId)
        + "&redirect_uri=" + Uri.EscapeDataString(options.RedirectUri)
        + "&scope=" + Uri.EscapeDataString(string.Join(' ', options.Scopes))
        + "&state=" + Uri.EscapeDataString(state)
        + "&code_challenge=" + Uri.EscapeDataString(challenge)
        + "&code_challenge_method=" + OAuthClient.ChallengeMethod;

    /// <summary>The failure redirect target. Never carries provider text — only a code this library
    /// owns.</summary>
    private static string Failure(SignInOptions options, string code) =>
        options.FailurePath
        + (options.FailurePath.Contains('?', StringComparison.Ordinal) ? "&" : "?")
        + SignInQuery.Error + "=" + Uri.EscapeDataString(code);

    private static string CodeFor(SignInOutcome outcome) =>
        outcome == SignInOutcome.Unavailable
            ? SignInFailureCode.ProviderUnavailable
            : SignInFailureCode.SignInFailed;

    /// <summary>
    /// Runs the consumer's policy, failing closed on anything it throws.
    /// </summary>
    /// <remarks>
    /// A broken policy refuses. The other reading — treat a throw as acceptance — makes a consumer's
    /// bug silently reopen the seam it added the policy to close, with nothing in the response to
    /// say so.
    /// <para>
    /// A cancellation the CLIENT caused is rethrown rather than reported: nobody is reading the
    /// response. One the POLICY caused is a broken policy and refuses like any other throw.
    /// </para>
    /// </remarks>
    private static async Task<bool> Allowed(
        HttpContext context, SignInPolicy policy, ILoggerFactory loggers)
    {
        try
        {
            return await policy(context, context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Consumer code, on an unauthenticated path: fail closed.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogPolicyThrew(loggers.CreateLogger(typeof(SignInEndpoints)), exception);
            return false;
        }
    }

    private static async Task<bool> ValidatedAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        ILoggerFactory loggers)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            return true;
        }
        catch (AntiforgeryValidationException exception)
        {
            // Validated here rather than relying on the consumer having called UseAntiforgery. Not
            // explained to the CALLER: a failed token is either a stale tab or a cross-site post,
            // and neither is improved by a page that then mints a fresh token for whoever asked.
            //
            // Logged at Debug for the OPERATOR, because the other reading of a bare 400 is a
            // consumer form that never emitted a token pair — every sign-out refused, with nothing
            // anywhere to read. Debug, not Warning: a stale tab is routine.
            LogAntiforgeryRejected(loggers.CreateLogger(typeof(SignInEndpoints)), exception);
            return false;
        }
    }

    /// <summary>A generous bound on <c>code</c> and <c>state</c>. State is minted here at 43
    /// characters and an authorization code is rarely longer.</summary>
    private const int MaxCallbackFieldLength = 512;

    [LoggerMessage(1, LogLevel.Warning, "The token exchange for a sign-in did not succeed ({Outcome}).")]
    private static partial void LogExchangeFailed(ILogger logger, string outcome);

    [LoggerMessage(2, LogLevel.Warning, "userinfo did not return a subject for an accepted token ({Outcome}).")]
    private static partial void LogUserInfoFailed(ILogger logger, string outcome);

    [LoggerMessage(3, LogLevel.Error, "The flow store reported a taken flow but carried no usable flow with it. TakeAsync must return FlowTakeResult.Taken only with a non-null verifier and returnTo.")]
    private static partial void LogStoreReturnedNoFlow(ILogger logger);

    [LoggerMessage(4, LogLevel.Debug, "An antiforgery token was rejected on a sign-in endpoint.")]
    private static partial void LogAntiforgeryRejected(ILogger logger, Exception ex);

    [LoggerMessage(5, LogLevel.Warning, "Sign-in was requested but no provider is configured.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(6, LogLevel.Error, "The registered SignInPolicy threw. The attempt was refused; a policy that throws must not be read as acceptance.")]
    private static partial void LogPolicyThrew(ILogger logger, Exception ex);

    [LoggerMessage(7, LogLevel.Warning, "A callback arrived whose state did not match the browser's own. The attempt was refused.")]
    private static partial void LogFlowCookieMismatch(ILogger logger);
}

/// <summary>
/// The machine-readable codes the library redirects to the consumer's sign-in page with, as
/// <c>?error=</c>.
/// </summary>
/// <remarks>
/// NO provider-supplied string ever reaches a query string, a header or a response body: not
/// <c>error_description</c>, not <c>error</c>, not an exception message. All of that is upstream
/// prose, and text from the provider rendered into a consumer's page is text the provider chose. It
/// goes to <see cref="ILogger"/> and nowhere else.
/// <para>
/// The consumer maps each code to its own words, in its own markup, in its own language.
/// </para>
/// </remarks>
public static class SignInFailureCode
{
    /// <summary>The callback arrived without a code or without a state.</summary>
    public const string CallbackIncomplete = "callback_incomplete";

    /// <summary>The state had already been consumed. A callback works exactly once.</summary>
    public const string StateUsed = "state_used";

    /// <summary>The state was never used but its lifetime had passed.</summary>
    public const string StateExpired = "state_expired";

    /// <summary>No such flow: never started, already swept, or fabricated.</summary>
    public const string StateUnknown = "state_unknown";

    /// <summary>The provider answered and declined. Retrying the same callback changes nothing.</summary>
    public const string SignInFailed = "signin_failed";

    /// <summary>The provider did not answer, or is not configured. Degrade and retry later.</summary>
    public const string ProviderUnavailable = "provider_unavailable";

    /// <summary>The consumer's own policy refused. No session was minted.</summary>
    public const string Refused = "refused";

    /// <summary>
    /// The callback's <c>state</c> did not match the one this browser started the flow with, or
    /// this browser started no flow. A sign-in begun in one browser cannot be finished in another.
    /// </summary>
    public const string StateMismatch = "state_mismatch";
}

/// <summary>
/// The query-string keys the library reads and writes. Named, because a consumer reading
/// <c>?error=</c> on its own sign-in page is reading a contract, and a contract spelled as a string
/// literal in two repositories drifts.
/// </summary>
public static class SignInQuery
{
    /// <summary>Carries a <see cref="SignInFailureCode"/> on the failure redirect.</summary>
    public const string Error = "error";

    /// <summary>Where to go after sign-in. Sanitised by the library on the way in and out.</summary>
    public const string ReturnTo = "returnTo";

    /// <summary>The provider's one-time code, on the callback.</summary>
    public const string Code = "code";

    /// <summary>The state minted when the flow started, on the callback.</summary>
    public const string State = "state";
}
