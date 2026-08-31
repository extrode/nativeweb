using System.Net;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using NativeWeb.Security;
using NativeWeb.SignIn;

using Xunit.Sdk;

namespace NativeWeb.SignIn.Tests.Unit;

internal static class OAuthClientFixtures
{
    internal static T Value<T>(SignInResult<T> result)
        where T : class =>
        result.Match(
            value => value,
            reason => throw new XunitException($"refused: {reason}"),
            () => throw new XunitException("unavailable"));

    internal static string Decode(string base64) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(base64));

    internal static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    internal static SignInOptions Options(
        ClientAuthenticationMode mode = ClientAuthenticationMode.ClientSecretBasic,
        string introspectionEndpoint = "https://provider.test/introspect")
    {
        var options = new SignInOptions
        {
            AuthorizationEndpoint = "https://provider.test/authorize",
            TokenEndpoint = "https://provider.test/token",
            UserInfoEndpoint = "https://provider.test/userinfo",
            IntrospectionEndpoint = introspectionEndpoint,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://app.test/signin/callback",
            ClientAuthentication = mode,
            StoreEncryptionKey = RandomNumberGenerator.GetBytes(StoreDataProtector.KeyBytes),
        };
        options.Validate();

        return options;
    }

    internal static OAuthClient ClientFor(
        StubHandler handler,
        ClientAuthenticationMode mode = ClientAuthenticationMode.ClientSecretBasic,
        string introspectionEndpoint = "https://provider.test/introspect") =>
        new(new HttpClient(handler), Options(mode, introspectionEndpoint), NullLogger<OAuthClient>.Instance);
}

/// <summary>
/// One canned response, and a record of what was asked for. Enough to assert on the wire without
/// a socket: every claim about a request this library makes is checked here, not against a live
/// provider.
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    private readonly Exception? _throw;

    internal StubHandler(HttpResponseMessage response) => _responses.Enqueue(response);

    internal StubHandler(params HttpResponseMessage[] responses)
    {
        foreach (HttpResponseMessage response in responses)
        {
            _responses.Enqueue(response);
        }
    }

    internal StubHandler(Exception toThrow) => _throw = toThrow;

    internal int Calls { get; private set; }

    internal HttpMethod? Method { get; private set; }

    internal string? RequestUri { get; private set; }

    internal string Body { get; private set; } = "";

    internal string? AuthorizationScheme { get; private set; }

    internal string? AuthorizationParameter { get; private set; }

    internal Dictionary<string, string> Form { get; } = new(StringComparer.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Method = request.Method;
        RequestUri = request.RequestUri?.ToString();
        AuthorizationScheme = request.Headers.Authorization?.Scheme;
        AuthorizationParameter = request.Headers.Authorization?.Parameter;
        Form.Clear();

        if (request.Content is not null)
        {
            Body = await request.Content.ReadAsStringAsync(cancellationToken);
            foreach (string pair in Body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = pair.Split('=', 2);
                Form[Uri.UnescapeDataString(parts[0])] =
                    parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            }
        }

        if (_throw is not null)
        {
            throw _throw;
        }

        return _responses.Count == 1 ? _responses.Peek() : _responses.Dequeue();
    }
}
