using System.Security.Cryptography;

using Microsoft.AspNetCore.Http;

using NativeWeb.Security;

namespace NativeWeb.SignIn;

/// <summary>How the client presents its credentials at the token and introspection endpoints.</summary>
/// <remarks>
/// The three names track RFC 8414's <c>token_endpoint_auth_methods_supported</c> values, so a
/// provider's own metadata document says which of these to configure without a translation table.
/// </remarks>
public enum ClientAuthenticationMode
{
    /// <summary>HTTP Basic, credentials in the <c>Authorization</c> header. The RFC 6749 default.</summary>
    ClientSecretBasic,

    /// <summary><c>client_id</c> and <c>client_secret</c> as form fields in the request body.</summary>
    ClientSecretPost,

    /// <summary>
    /// No client credentials. For a public client, where PKCE and the registered redirect URI
    /// are the whole of the client's identity.
    /// </summary>
    None,
}

/// <summary>
/// Everything the library needs from its consumer: the provider's endpoints, the client identity,
/// the routes it should own, and the key it encrypts stored provider tokens with.
/// </summary>
/// <remarks>
/// Two kinds of wrong are distinguished on purpose. <see cref="IsConfigured"/> is DEGRADATION —
/// a box with no provider credentials must still boot and still serve its public pages, and
/// sign-in reports itself unavailable. <see cref="Validate"/> is MISCONFIGURATION — a cookie
/// named <c>session</c>, a fallback pointing off-site, or half a provider's settings is not a
/// degraded box, it is a broken one, and it fails at startup.
/// <para>
/// There is deliberately no settable <c>Secure</c>, <c>HttpOnly</c> or <c>SameSite</c>. Those are
/// not choices. The library sets <c>Secure=true</c>, <c>HttpOnly=true</c> and <c>SameSite=Lax</c>
/// on the session cookie, and a consumer does not get to weaken them. Lax rather than Strict
/// because the browser arrives here through the provider's cross-site redirect, and under Strict
/// the cookie would not be sent on that navigation.
/// </para>
/// <para>
/// Nothing here names a provider. Every endpoint is configuration, and the defaults are either
/// blank or a route inside this application.
/// </para>
/// </remarks>
public sealed class SignInOptions
{
    private const string HostPrefix = "__Host-";

    /// <summary>Where the browser is sent to begin authorization. Absolute http or https URL.</summary>
    public string AuthorizationEndpoint { get; set; } = "";

    /// <summary>Where the authorization code is exchanged and refresh tokens are redeemed.</summary>
    public string TokenEndpoint { get; set; } = "";

    /// <summary>Where the subject and profile claims are read after exchange.</summary>
    public string UserInfoEndpoint { get; set; } = "";

    /// <summary>
    /// Where a bearer token is verified. Optional: a product that maps no API endpoints behind
    /// <c>BearerIntrospectionFilter</c> never calls it, so an absent value is not a
    /// misconfiguration.
    /// </summary>
    public string IntrospectionEndpoint { get; set; } = "";

    /// <summary>The OAuth client identifier issued by the provider.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// The OAuth client secret. From user-secrets or the environment, never from a settings file.
    /// Unused, and not required, when <see cref="ClientAuthentication"/> is
    /// <see cref="ClientAuthenticationMode.None"/>.
    /// </summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// The callback registered with the provider. The provider matches this against its
    /// registered list, so it is configuration rather than something derived from the incoming
    /// request — deriving it would mean a <c>Host</c> header could choose where authorization
    /// returns to.
    /// </summary>
    public string RedirectUri { get; set; } = "";

    /// <summary>The scopes requested at the authorization endpoint.</summary>
    public IList<string> Scopes { get; } = ["openid", "profile", "email"];

    /// <summary>How the client authenticates at the token and introspection endpoints.</summary>
    public ClientAuthenticationMode ClientAuthentication { get; set; } = ClientAuthenticationMode.ClientSecretBasic;

    /// <summary>How long a started sign-in stays usable. Default 15 minutes.</summary>
    public TimeSpan FlowLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a minted session is accepted for. Default 14 days.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// How far ahead of an access token's expiry a request refreshes it. Default 2 minutes.
    /// </summary>
    /// <remarks>
    /// Zero means refresh only once the token has already expired, which puts every in-flight
    /// request that raced the expiry into the refresh path at once. A skew wide enough to cover
    /// the round trip to the provider keeps the refresh off the critical path of a request that
    /// still holds a usable token.
    /// </remarks>
    public TimeSpan RefreshSkew { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long an introspection result is cached. Default 30 seconds; zero disables caching.
    /// </summary>
    /// <remarks>
    /// This is the window in which a token revoked at the provider is still accepted here, so it
    /// trades a provider round trip per API request against that window. It never extends a
    /// token's life past the expiry the provider reported.
    /// </remarks>
    public TimeSpan IntrospectionCacheLifetime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether the provider's access and refresh tokens are kept for the life of the session.
    /// Default <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Set <see langword="false"/> for a product that reads the subject once at sign-in and never
    /// calls the provider again: the tokens are discarded after userinfo, so there is nothing to
    /// refresh and nothing to leak, and the session ends at <see cref="SessionLifetime"/> rather
    /// than at the provider's word.
    /// </remarks>
    public bool RetainTokens { get; set; } = true;

    /// <summary>
    /// The session cookie's name. MUST start with <c>__Host-</c>.
    /// </summary>
    /// <remarks>
    /// The prefix is not decoration: without it, any sibling subdomain can set this cookie over
    /// plain HTTP, and the session cookie becomes attacker-writable. A name that does not carry
    /// it is rejected by <see cref="Validate"/> at startup.
    /// </remarks>
    public string CookieName { get; set; } = "__Host-signin";

    /// <summary>Fixed: the session cookie is never readable from script.</summary>
    public static bool CookieHttpOnly => true;

    /// <summary>Fixed: the session cookie is never sent over plain HTTP.</summary>
    public static bool CookieSecure => true;

    /// <summary>
    /// Fixed: <c>Lax</c>, the strictest setting under which the cookie survives the provider's
    /// cross-site redirect back to the callback.
    /// </summary>
    public static SameSiteMode CookieSameSite => SameSiteMode.Lax;

    /// <summary>
    /// The <see cref="StoreDataProtector.KeyBytes"/>-byte key the provider's tokens are encrypted
    /// with before they are handed to the session store.
    /// </summary>
    /// <remarks>
    /// No key and no <see cref="AllowEphemeralStoreEncryptionKey"/> throws at startup. Silently
    /// generating one means a production app that forgot a config entry boots fine, signs everyone
    /// out on restart and on every scale-out, and signals it only in a log line nobody reads.
    /// </remarks>
    public byte[]? StoreEncryptionKey { get; set; }

    /// <summary>
    /// Permits <see cref="Validate"/> to generate a random key when none was supplied.
    /// DEVELOPMENT ONLY: the key lives in memory, so every restart — and every additional
    /// instance — signs everyone out.
    /// </summary>
    public bool AllowEphemeralStoreEncryptionKey { get; set; }

    /// <summary>Where the library's login endpoint lives. The consumer chooses.</summary>
    public string LoginPath { get; set; } = "/signin";

    /// <summary>Where the provider returns. Must match <see cref="RedirectUri"/>'s path.</summary>
    public string CallbackPath { get; set; } = "/signin/callback";

    /// <summary>Where the library's sign-out endpoint lives.</summary>
    public string LogoutPath { get; set; } = "/signout";

    /// <summary>Where a signed-out browser is sent.</summary>
    public string SignedOutPath { get; set; } = "/";

    /// <summary>
    /// Where a failed sign-in lands, carrying <c>?error=</c> and a <see cref="SignInFailureCode"/>.
    /// A page the CONSUMER owns and renders.
    /// </summary>
    /// <remarks>
    /// It cannot be <see cref="LoginPath"/>, and that is the whole reason this setting exists. The
    /// library owns <c>GET LoginPath</c> — that route IS the flow trigger — and the handler never
    /// reads <c>?error=</c>, so a failure redirected there is a request to start the flow again.
    /// An unconfigured provider, a refusing <c>SignInPolicy</c> and a resolver returning null all
    /// fail the same way on every attempt, so the browser bounces until it gives up:
    /// <c>ERR_TOO_MANY_REDIRECTS</c>, no page, and the failure code delivered to a handler that
    /// discards it. <see cref="Validate"/> refuses that pairing rather than leaving it to be found
    /// in production.
    /// <para>
    /// The default is <see cref="DefaultReturnPath"/>'s value, <c>"/"</c>: a page the consumer
    /// certainly has, so the default never loops. A consumer that does not read <c>?error=</c>
    /// there shows a home page instead of an explanation — the failure is silent, not broken. Point
    /// this at a real page and read the code.
    /// </para>
    /// </remarks>
    public string FailurePath { get; set; } = "/";

    /// <summary>
    /// Where sign-in lands when the requested return path is missing or off-site. Validated with
    /// <see cref="ReturnPath.IsUsableFallback"/>: a fallback of <c>https://evil.test</c> would
    /// make every rejected candidate redirect off-site, which is the open redirect wearing the
    /// guard's clothes.
    /// </summary>
    public string DefaultReturnPath { get; set; } = "/";

    /// <summary>
    /// True when there is enough here to talk to a provider at all. Degradation, not
    /// misconfiguration: a box without credentials still boots and still serves its public site;
    /// only sign-in degrades, and it says so rather than throwing.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AuthorizationEndpoint)
        && !string.IsNullOrWhiteSpace(TokenEndpoint)
        && !string.IsNullOrWhiteSpace(UserInfoEndpoint)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(RedirectUri)
        && (ClientAuthentication == ClientAuthenticationMode.None
            || !string.IsNullOrWhiteSpace(ClientSecret));

    /// <summary>
    /// Checks everything that must be right for the library to be safe, and mints an ephemeral
    /// encryption key when one was asked for. Called during registration, so a bad cookie name
    /// fails at startup rather than at the first request.
    /// </summary>
    /// <exception cref="InvalidOperationException">Any of the security-critical settings is wrong.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CookieName) || !CookieName.StartsWith(HostPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{nameof(CookieName)} must start with \"{HostPrefix}\". Without that prefix any sibling "
                + "subdomain can set the cookie over plain HTTP, which makes the session cookie "
                + "attacker-writable.");
        }

        ValidateEncryptionKey();

        if (!ReturnPath.IsUsableFallback(DefaultReturnPath))
        {
            throw new InvalidOperationException(
                $"{nameof(DefaultReturnPath)} must be a same-site relative path beginning with a single "
                + "'/'. An off-site fallback re-opens the redirect the sanitiser exists to close.");
        }

        // The four routes the library owns. Same guard as the fallback, for the same reason:
        // LogoutPath and SignedOutPath are redirect targets too, and a SignedOutPath of
        // "https://evil.test" would send every signed-out browser there.
        RequireRelative(LoginPath, nameof(LoginPath));
        RequireRelative(CallbackPath, nameof(CallbackPath));
        RequireRelative(LogoutPath, nameof(LogoutPath));
        RequireRelative(SignedOutPath, nameof(SignedOutPath));
        RequireRelative(FailurePath, nameof(FailurePath));

        // The two routes the library owns as GET endpoints. Either one as the failure target is an
        // infinite redirect: LoginPath restarts the flow and CallbackPath refuses again, both
        // redirecting straight back here.
        if (string.Equals(FailurePath, LoginPath, StringComparison.Ordinal)
            || string.Equals(FailurePath, CallbackPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{nameof(FailurePath)} is \"{FailurePath}\", which is a route this library owns. A "
                + "failure redirected to the library's own GET endpoint restarts the flow and fails "
                + "again, so the browser loops until it gives up and the error code reaches nothing "
                + $"that reads it. Point {nameof(FailurePath)} at a page the application renders.");
        }

        RequirePositive(FlowLifetime, nameof(FlowLifetime));
        RequirePositive(SessionLifetime, nameof(SessionLifetime));

        if (RefreshSkew < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(RefreshSkew)} cannot be negative.");
        }

        if (IntrospectionCacheLifetime < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(IntrospectionCacheLifetime)} cannot be negative. Zero disables caching.");
        }

        ValidateEndpoints();
        ValidateRedirectPairing();
    }

    private void ValidateEncryptionKey()
    {
        if (StoreEncryptionKey is null)
        {
            if (!AllowEphemeralStoreEncryptionKey)
            {
                throw new InvalidOperationException(
                    $"{nameof(StoreEncryptionKey)} is required. Set "
                    + $"{nameof(AllowEphemeralStoreEncryptionKey)} only in development: an in-memory key "
                    + "signs every user out on restart and on every scale-out.");
            }

            StoreEncryptionKey = RandomNumberGenerator.GetBytes(StoreDataProtector.KeyBytes);
            return;
        }

        if (StoreEncryptionKey.Length != StoreDataProtector.KeyBytes)
        {
            throw new InvalidOperationException(
                $"{nameof(StoreEncryptionKey)} must be exactly {StoreDataProtector.KeyBytes} bytes. Derive "
                + $"one from a configured secret with {nameof(StoreDataProtector)}.{nameof(StoreDataProtector.DeriveKey)}.");
        }

        if (IsConstant(StoreEncryptionKey))
        {
            // Length is not entropy. `new byte[32]` is the shortest thing that satisfies a length
            // check, it is what a placeholder config and a copied example both look like, and it
            // boots a host whose every stored provider token anyone can decrypt. A full entropy
            // estimate on 32 bytes is not meaningful; refusing the one shape that is always a
            // mistake is.
            throw new InvalidOperationException(
                $"{nameof(StoreEncryptionKey)} is a single repeated byte, which is a placeholder rather "
                + "than a key: every provider token it protects can be decrypted by anyone who guesses "
                + "it. Generate one with RandomNumberGenerator.GetBytes(32).");
        }
    }

    private void ValidateEndpoints()
    {
        // Blank everywhere is the unconfigured box, which must still boot. Blank in SOME places
        // is nobody's intent: it is a half-copied settings block, and left unchecked it surfaces
        // as a null-reference or a UriFormatException at the first sign-in attempt — a stack
        // trace at 3am instead of a message at startup.
        if (!IsConfigured)
        {
            bool anythingSet =
                !string.IsNullOrWhiteSpace(AuthorizationEndpoint)
                || !string.IsNullOrWhiteSpace(TokenEndpoint)
                || !string.IsNullOrWhiteSpace(UserInfoEndpoint)
                || !string.IsNullOrWhiteSpace(IntrospectionEndpoint)
                || !string.IsNullOrWhiteSpace(ClientId)
                || !string.IsNullOrWhiteSpace(ClientSecret)
                || !string.IsNullOrWhiteSpace(RedirectUri);

            if (anythingSet)
            {
                throw new InvalidOperationException(
                    "Sign-in is partly configured, which is never intentional. Missing: "
                    + string.Join(", ", MissingSettings())
                    + $". Leave every one of them blank to run with sign-in unavailable, or set all of them.");
            }
        }

        RequireAbsoluteHttp(AuthorizationEndpoint, nameof(AuthorizationEndpoint));
        RequireAbsoluteHttp(TokenEndpoint, nameof(TokenEndpoint));
        RequireAbsoluteHttp(UserInfoEndpoint, nameof(UserInfoEndpoint));
        RequireAbsoluteHttp(IntrospectionEndpoint, nameof(IntrospectionEndpoint));
    }

    private void ValidateRedirectPairing()
    {
        if (string.IsNullOrWhiteSpace(RedirectUri))
        {
            return;
        }

        if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out Uri? redirect)
            || (redirect.Scheme != Uri.UriSchemeHttp && redirect.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"{nameof(RedirectUri)} must be an absolute http or https URL, or blank to leave sign-in "
                + "unconfigured.");
        }

        // The pairing. RedirectUri is what the provider sends the browser back to; CallbackPath
        // is where this library listens. They are set in two places by two people, and when they
        // disagree nothing fails until a customer finishes signing in and lands on a 404 — with
        // no error, no log line, and a flow row that sits there until it is swept.
        if (!string.Equals(redirect.AbsolutePath, CallbackPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{nameof(RedirectUri)} points at \"{redirect.AbsolutePath}\" but {nameof(CallbackPath)} "
                + $"is \"{CallbackPath}\". The provider returns the customer to the first; only the "
                + "second is mapped. They must be the same path.");
        }
    }

    private IEnumerable<string> MissingSettings()
    {
        if (string.IsNullOrWhiteSpace(AuthorizationEndpoint))
        {
            yield return nameof(AuthorizationEndpoint);
        }

        if (string.IsNullOrWhiteSpace(TokenEndpoint))
        {
            yield return nameof(TokenEndpoint);
        }

        if (string.IsNullOrWhiteSpace(UserInfoEndpoint))
        {
            yield return nameof(UserInfoEndpoint);
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            yield return nameof(ClientId);
        }

        if (string.IsNullOrWhiteSpace(RedirectUri))
        {
            yield return nameof(RedirectUri);
        }

        if (ClientAuthentication != ClientAuthenticationMode.None && string.IsNullOrWhiteSpace(ClientSecret))
        {
            yield return nameof(ClientSecret);
        }
    }

    /// <summary>True when every byte is the same — the shape of a placeholder key.</summary>
    private static bool IsConstant(byte[] key)
    {
        foreach (byte b in key)
        {
            if (b != key[0])
            {
                return false;
            }
        }

        return true;
    }

    private static void RequireRelative(string value, string name)
    {
        if (!ReturnPath.IsUsableFallback(value))
        {
            throw new InvalidOperationException(
                $"{name} must be a same-site relative path beginning with a single '/'.");
        }
    }

    private static void RequirePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{name} must be positive.");
        }
    }

    private static void RequireAbsoluteHttp(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"{name} must be an absolute http or https URL, or blank to leave sign-in unconfigured.");
        }
    }
}
