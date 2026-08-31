using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace NativeWeb.SignIn.Tests.Integration;

/// <summary>
/// An authorization server that runs inside the test host (R27).
/// </summary>
/// <remarks>
/// It is a stub and not a conforming implementation: it validates the PKCE verifier against the
/// challenge it was sent, refuses a code twice, and answers introspection, which is what the
/// relying party's behaviour depends on. It does not issue an id_token, does not sign anything,
/// and has no consent screen — none of which the client half reads.
/// </remarks>
public sealed class StubProvider
{
    private readonly ConcurrentDictionary<string, Grant> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _accessTokens = new(StringComparer.Ordinal);

    /// <summary>The subject the next sign-in will be for.</summary>
    public string Subject { get; set; } = "provider-subject-1";

    /// <summary>The address the userinfo endpoint reports, or null for none.</summary>
    public string? Email { get; set; } = "signed-in@example.test";

    /// <summary>How many token requests have been made. Zero proves a refusal cost no round trip.</summary>
    public int TokenRequests { get; private set; }

    /// <summary>Set to refuse the authorization request, as a user who declines would.</summary>
    public string? RefuseWith { get; set; }

    /// <summary>Set to make the token endpoint fail, as an unreachable provider would.</summary>
    public bool TokenEndpointIsDown { get; set; }

    /// <summary>Access tokens introspection should report inactive.</summary>
    public HashSet<string> Revoked { get; } = new(StringComparer.Ordinal);

    /// <summary>Mounts the provider's four endpoints under <c>/stub</c>.</summary>
    /// <param name="endpoints">The host's route builder.</param>
    public void MapStub(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/stub/authorize", (HttpContext http, CancellationToken ct) =>
            Task.FromResult(Authorize(http)));
        endpoints.MapPost("/stub/token", (HttpContext http, CancellationToken ct) => TokenAsync(http));
        endpoints.MapGet("/stub/userinfo", (HttpContext http, CancellationToken ct) =>
            Task.FromResult(UserInfo(http)));
        endpoints.MapPost("/stub/introspect", (HttpContext http, CancellationToken ct) =>
            IntrospectAsync(http));
    }

    private IResult Authorize(HttpContext http)
    {
        string state = http.Request.Query["state"].ToString();
        string redirect = http.Request.Query["redirect_uri"].ToString();

        if (RefuseWith is { Length: > 0 } refusal)
        {
            return Results.Redirect(
                $"{redirect}?error={Uri.EscapeDataString(refusal)}&state={Uri.EscapeDataString(state)}");
        }

        string code = Guid.NewGuid().ToString("N");
        _codes[code] = new Grant(
            http.Request.Query["code_challenge"].ToString(),
            http.Request.Query["client_id"].ToString());

        return Results.Redirect(
            $"{redirect}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");
    }

    private async Task<IResult> TokenAsync(HttpContext http)
    {
        TokenRequests++;

        if (TokenEndpointIsDown)
        {
            return Results.StatusCode((int)HttpStatusCode.ServiceUnavailable);
        }

        IFormCollection form = await http.Request.ReadFormAsync().ConfigureAwait(false);
        string code = form["code"].ToString();

        if (!_codes.TryRemove(code, out Grant? grant))
        {
            return Results.BadRequest(new { error = "invalid_grant" });
        }

        if (Challenge(form["code_verifier"].ToString()) != grant.Challenge)
        {
            return Results.BadRequest(new { error = "invalid_grant" });
        }

        string accessToken = Guid.NewGuid().ToString("N");
        _accessTokens[accessToken] = Subject;

        return Results.Json(new
        {
            access_token = accessToken,
            refresh_token = Guid.NewGuid().ToString("N"),
            token_type = "Bearer",
            expires_in = 1800,
        });
    }

    private IResult UserInfo(HttpContext http)
    {
        string presented = Bearer(http);

        return _accessTokens.TryGetValue(presented, out string? subject)
            ? Results.Json(new { sub = subject, email = Email })
            : Results.Unauthorized();
    }

    private async Task<IResult> IntrospectAsync(HttpContext http)
    {
        IFormCollection form = await http.Request.ReadFormAsync().ConfigureAwait(false);
        string token = form["token"].ToString();

        return _accessTokens.TryGetValue(token, out string? subject) && !Revoked.Contains(token)
            ? Results.Json(new { active = true, sub = subject })
            : Results.Json(new { active = false });
    }

    /// <summary>Issues an access token directly, for the bearer-filter cases.</summary>
    /// <param name="subject">Who the token is for.</param>
    /// <returns>The token to present as a bearer credential.</returns>
    public string IssueAccessToken(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        string token = Guid.NewGuid().ToString("N");
        _accessTokens[token] = subject;

        return token;
    }

    private static string Bearer(HttpContext http)
    {
        string header = http.Request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : "";
    }

    private static string Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record Grant(string Challenge, string ClientId);
}
