using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.Logging;

namespace NativeWeb.SignIn;

/// <summary>
/// The client half of the handshake: exchange an authorization code for tokens, and read the
/// subject behind an access token.
/// </summary>
/// <remarks>
/// Client credentials are attached per request rather than as a default header on the
/// <see cref="HttpClient"/>: a default header travels with every other request that client ever
/// makes, and a shared client is the normal registration.
/// <para>
/// Every endpoint is an absolute URL from <see cref="SignInOptions"/>. Nothing is derived from a
/// base address by string concatenation — two providers put the token endpoint at different
/// depths, and a library that guesses the path is a library that works against one provider.
/// </para>
/// <para>
/// The options are a plain instance rather than <c>IOptions&lt;T&gt;</c>: they are validated once
/// at registration, and an indirection here would only defer that failure to the first request.
/// </para>
/// </remarks>
/// <param name="http">The HTTP client, registered through <c>AddHttpClient</c>.</param>
/// <param name="options">Validated options.</param>
/// <param name="logger">Where provider error text goes, and the only place it goes.</param>
public sealed partial class OAuthClient(HttpClient http, SignInOptions options, ILogger<OAuthClient> logger)
{
    /// <summary>
    /// The only challenge method worth sending: <c>plain</c> puts the verifier on the wire at
    /// start time, which is the whole thing PKCE exists to avoid.
    /// </summary>
    public const string ChallengeMethod = "S256";

    /// <summary>
    /// True when there is enough configuration to talk to a provider at all. A box without
    /// credentials still boots and still serves its public pages; only sign-in degrades, and it
    /// says so rather than throwing. A consumer's login page reads this to render or suppress its
    /// button.
    /// </summary>
    public bool IsConfigured => options.IsConfigured;

    /// <summary>
    /// A fresh PKCE verifier: 32 random bytes, base64url, 43 characters of the RFC 7636
    /// unreserved set.
    /// </summary>
    /// <returns>The code verifier. Secret; never logged.</returns>
    public static string NewVerifier() => RandomToken(32);

    /// <summary>
    /// The <c>state</c> parameter, which is also the primary key of the stored flow. Same 256 bits
    /// as the verifier for the same reason: anyone who can guess it can complete somebody else's
    /// sign-in.
    /// </summary>
    /// <returns>The state parameter.</returns>
    public static string NewState() => RandomToken(32);

    /// <summary>The S256 code challenge for a verifier.</summary>
    /// <param name="verifier">A verifier from <see cref="NewVerifier"/>.</param>
    /// <returns>The base64url-encoded SHA-256 digest, unpadded.</returns>
    public static string Challenge(string verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);

        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    /// <summary>
    /// Exchanges an authorization code for tokens.
    /// </summary>
    /// <remarks>
    /// A refusal here is the normal shape of a replayed or expired callback — a provider answers
    /// <c>400 invalid_grant</c> for a code that is used, expired, unknown, PKCE-mismatched or
    /// bound to another client. All of those are one thing to the customer, and they arrive as
    /// <see cref="RefusalReason.InvalidGrant"/>.
    /// </remarks>
    /// <param name="code">The code from the callback.</param>
    /// <param name="verifier">The PKCE verifier stored when the flow started.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token pair.</returns>
    public async Task<SignInResult<ProviderTokens>> ExchangeCodeAsync(
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return SignInResult<ProviderTokens>.Unavailable("not_configured");
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            // Exactly the value sent to the authorization endpoint. The provider compares them,
            // and that comparison is the reason this is configuration rather than something
            // rebuilt from the current request.
            ["redirect_uri"] = options.RedirectUri,
        };

        using HttpRequestMessage request = TokenRequest(options.TokenEndpoint, fields);

        return await SendAsync(
            request,
            OAuthJsonContext.Default.TokenResponse,
            static parsed => ProviderTokens.From(parsed),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the subject behind an access token.
    /// </summary>
    /// <remarks>
    /// The local account is keyed on the subject and never on the email — an address can move
    /// between people, a subject cannot, and keying on email would hand a re-registered address
    /// somebody else's account.
    /// </remarks>
    /// <param name="accessToken">From <see cref="ExchangeCodeAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The subject and the contact address that proves nothing.</returns>
    public async Task<SignInResult<ProviderUser>> GetUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return SignInResult<ProviderUser>.Unavailable("not_configured");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.UserInfoEndpoint));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await SendAsync(
            request,
            OAuthJsonContext.Default.UserInfo,
            static parsed => parsed.Sub is null ? null : new ProviderUser(parsed.Sub, parsed.Email, parsed.Name),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Redeems a refresh token for a fresh pair.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="ProviderTokens.RefreshToken"/> is the one to store from here on.
    /// A provider that rotates issues a new refresh token on every redemption and treats a
    /// re-presented one as theft, revoking the whole family — so keeping the old value after a
    /// successful refresh is what ends a healthy session, and the caller must replace it.
    /// <para>
    /// A refusal is final: <c>invalid_grant</c> means this session cannot be continued, and the
    /// caller ends it rather than retrying. Only <see cref="SignInOutcome.Unavailable"/> is worth
    /// a second attempt.
    /// </para>
    /// </remarks>
    /// <param name="refreshToken">The refresh token stored for this session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rotated token pair.</returns>
    public async Task<SignInResult<ProviderTokens>> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return SignInResult<ProviderTokens>.Unavailable("not_configured");
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };

        using HttpRequestMessage request = TokenRequest(options.TokenEndpoint, fields);

        return await SendAsync(
            request,
            OAuthJsonContext.Default.TokenResponse,
            static parsed => ProviderTokens.From(parsed),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the provider whether a bearer token is live, per RFC 7662.
    /// </summary>
    /// <remarks>
    /// An inactive token is a successful call that reports <c>active: false</c>, not a refusal:
    /// the provider answered, and the answer is a fact about the token rather than a rejection of
    /// this client. Refusals here are about the CLIENT — bad credentials, malformed request — and
    /// a caller that conflates the two turns an outage into "everyone's token is invalid".
    /// </remarks>
    /// <param name="token">The bearer token presented by the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the provider says about the token.</returns>
    public async Task<SignInResult<TokenIntrospection>> IntrospectAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(options.IntrospectionEndpoint))
        {
            return SignInResult<TokenIntrospection>.Unavailable("not_configured");
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["token"] = token,
        };

        using HttpRequestMessage request = TokenRequest(options.IntrospectionEndpoint, fields);

        return await SendAsync(
            request,
            OAuthJsonContext.Default.IntrospectionResponse,
            static parsed => TokenIntrospection.From(parsed),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One send, one classification. Everything that is not a 2xx with a parseable body is either
    /// a refusal or an outage, and this is the single place that decides which — so a new call
    /// site cannot invent a fourth answer.
    /// </summary>
    private async Task<SignInResult<TOut>> SendAsync<TWire, TOut>(
        HttpRequestMessage request,
        JsonTypeInfo<TWire> wireType,
        Func<TWire, TOut?> project,
        CancellationToken cancellationToken)
        where TWire : class
        where TOut : class
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            // Connection refused, DNS, TLS. Nothing about the customer's input.
            LogTransportFailure(logger, request.RequestUri?.AbsolutePath, exception);
            return SignInResult<TOut>.Unavailable("transport");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout. Distinguished from a real cancellation by the token, because HttpClient
            // reports both as the same exception type.
            LogTimeout(logger, request.RequestUri?.AbsolutePath, exception);
            return SignInResult<TOut>.Unavailable("timeout");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string? error = await ReadErrorAsync(response, request, cancellationToken).ConfigureAwait(false);
                LogRefused(logger, request.RequestUri?.AbsolutePath, (int)response.StatusCode, error ?? "(none sent)");

                // A 5xx is the provider having a bad day, not the customer having a bad link, so
                // it is retryable in a way a 4xx is not.
                return response.StatusCode >= HttpStatusCode.InternalServerError
                    ? SignInResult<TOut>.Unavailable(error)
                    : SignInResult<TOut>.Refused(RefusalReasons.From(error), error);
            }

            TWire? parsed;
            try
            {
                parsed = await response.Content
                    .ReadFromJsonAsync(wireType, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                // A 200 we cannot read is not a refusal: nothing the customer did produced it,
                // and retrying is the right advice.
                LogUnreadableBody(logger, request.RequestUri?.AbsolutePath, exception);
                return SignInResult<TOut>.Unavailable("unreadable");
            }

            if (parsed is null)
            {
                return SignInResult<TOut>.Unavailable("empty");
            }

            TOut? projected = project(parsed);
            return projected is null
                ? SignInResult<TOut>.Unavailable("empty")
                : SignInResult<TOut>.Ok(projected);
        }
    }

    /// <summary>
    /// The provider's <c>error</c> code, when it sent one. Best effort: an error body that will
    /// not parse changes nothing about the outcome, only about how much the log says.
    ///
    /// <para><c>error_description</c> is read, logged here and then dropped on the floor. It is
    /// never returned, never stored and never reaches a public member — it is upstream prose, and
    /// a value that leaves this method is a value that reaches a page.</para>
    /// </summary>
    private async Task<string?> ReadErrorAsync(
        HttpResponseMessage response,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            OAuthErrorResponse? body = await response.Content
                .ReadFromJsonAsync(OAuthJsonContext.Default.OAuthErrorResponse, cancellationToken)
                .ConfigureAwait(false);

            if (body?.ErrorDescription is { Length: > 0 } description)
            {
                LogErrorDescription(logger, request.RequestUri?.AbsolutePath, description);
            }

            return body?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // Non-JSON content type — a proxy's HTML error page, typically.
            return null;
        }
    }

    /// <summary>
    /// A form-encoded POST carrying the client's credentials the way the provider was configured
    /// to expect them.
    /// </summary>
    /// <remarks>
    /// The three modes are mutually exclusive and the credentials appear in exactly one place.
    /// Sending them twice is not belt-and-braces: RFC 6749 §2.3.1 says a client must not, and a
    /// provider that reads the body first will authenticate against a value the operator thought
    /// was unused.
    /// <para>
    /// Under <see cref="ClientAuthenticationMode.None"/> the client is public and there is no
    /// secret to send, but <c>client_id</c> still goes in the body: the provider has nothing else
    /// to identify the client with, and the code was issued against it.
    /// </para>
    /// </remarks>
    private HttpRequestMessage TokenRequest(string endpoint, Dictionary<string, string> fields)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint));

        switch (options.ClientAuthentication)
        {
            case ClientAuthenticationMode.ClientSecretBasic:
                string credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(
                        string.Create(CultureInfo.InvariantCulture, $"{options.ClientId}:{options.ClientSecret}")));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                break;

            case ClientAuthenticationMode.ClientSecretPost:
                fields["client_id"] = options.ClientId;
                fields["client_secret"] = options.ClientSecret;
                break;

            case ClientAuthenticationMode.None:
                fields["client_id"] = options.ClientId;
                break;

            default:
                throw new InvalidOperationException("Unrecognised client authentication mode.");
        }

        request.Content = new FormUrlEncodedContent(fields);
        return request;
    }

    [LoggerMessage(1, LogLevel.Warning, "The call to {Path} did not complete")]
    private static partial void LogTransportFailure(ILogger logger, string? path, Exception ex);

    [LoggerMessage(2, LogLevel.Warning, "The call to {Path} timed out")]
    private static partial void LogTimeout(ILogger logger, string? path, Exception ex);

    [LoggerMessage(3, LogLevel.Warning, "The provider refused {Path} with {Status} ({Error})")]
    private static partial void LogRefused(ILogger logger, string? path, int status, string error);

    [LoggerMessage(4, LogLevel.Warning, "The provider returned an unreadable body from {Path}")]
    private static partial void LogUnreadableBody(ILogger logger, string? path, Exception ex);

    [LoggerMessage(5, LogLevel.Warning, "The provider described its {Path} failure as: {Description}")]
    private static partial void LogErrorDescription(ILogger logger, string? path, string description);

    private static string RandomToken(int byteCount)
    {
        Span<byte> bytes = stackalloc byte[byteCount];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url(bytes);
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

/// <summary>What a token request yields.</summary>
/// <param name="AccessToken">The bearer token for <see cref="OAuthClient.GetUserInfoAsync"/>.</param>
/// <param name="RefreshToken">
/// The token that buys the next access token, when the provider issued one. Absent for a provider
/// that does not rotate, and absent when the requested scopes did not include offline access.
/// </param>
/// <param name="ExpiresIn">How long the access token is good for, in seconds, as the provider reported it.</param>
public sealed record ProviderTokens(string AccessToken, string? RefreshToken, int ExpiresIn)
{
    /// <summary>
    /// Reads a token response, or <see langword="null"/> when it carries no access token — a 200
    /// without one is not a success, whatever the status line said.
    /// </summary>
    internal static ProviderTokens? From(TokenResponse response) =>
        response.AccessToken is null
            ? null
            : new ProviderTokens(response.AccessToken, response.RefreshToken, response.ExpiresIn);

    /// <summary>
    /// Redacts both tokens. The compiler-generated <c>ToString</c> prints every field, so a
    /// consumer structured-logging this record — or putting it in an exception message — writes a
    /// live bearer token into logs. The same reasoning already redacts
    /// <see cref="LoginFlow.Verifier"/>, and these tokens outlive the verifier.
    /// </summary>
    public override string ToString() =>
        $"ProviderTokens {{ AccessToken = (redacted), RefreshToken = (redacted), ExpiresIn = {ExpiresIn} }}";
}

/// <summary>What the provider says about a bearer token.</summary>
/// <param name="Active">
/// Whether the token is live. The only field a refusal may be decided on, and the only one a
/// provider is required to send.
/// </param>
/// <param name="Subject">The subject the token was issued for, when the provider sent one.</param>
/// <param name="Scope">The space-delimited scopes the token carries, when the provider sent them.</param>
/// <param name="ExpiresAt">
/// When the token stops being valid, when the provider sent an <c>exp</c>. This is the ceiling on
/// how long the result may be cached: a cache that outlives it accepts a token the provider has
/// already retired.
/// </param>
public sealed record TokenIntrospection(
    bool Active,
    string? Subject,
    string? Scope,
    DateTimeOffset? ExpiresAt)
{
    internal static TokenIntrospection From(IntrospectionResponse response) =>
        new(
            response.Active,
            response.Sub,
            response.Scope,
            response.Exp is { } exp ? DateTimeOffset.FromUnixTimeSeconds(exp) : null);
}

/// <summary>Who the provider says the token holder is.</summary>
/// <param name="Subject">The provider's subject identifier. The only thing to key a local account on.</param>
/// <param name="Email">A contact address that proves nothing. May be absent.</param>
/// <param name="Name">A display name, when the provider sent one. May be absent.</param>
public sealed record ProviderUser(string Subject, string? Email, string? Name)
{
    /// <summary>
    /// Redacts <see cref="Email"/> and <see cref="Name"/>. Neither is a secret, but both are
    /// personal data, and a record whose <c>ToString</c> puts an address into every log line that
    /// mentions it is how personal data ends up somewhere nobody chose to put it. The subject is
    /// kept: it is an opaque identifier, and it is what an operator needs to correlate a sign-in.
    /// </summary>
    public override string ToString() =>
        $"ProviderUser {{ Subject = {Subject}, Email = (redacted), Name = (redacted) }}";
}
