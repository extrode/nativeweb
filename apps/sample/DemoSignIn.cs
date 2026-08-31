using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Antiforgery;

using NativeWeb.SignIn;

namespace NativeWeb.Sample;

/// <summary>
/// The sign-in demo (spec 016 R28): an authorization server inside this process, the
/// framework's relying party pointed at it, and one page on each side of the line.
/// </summary>
/// <remarks>
/// <para>
/// Development only, and mounted from <c>Program</c> inside the same environment gate as the
/// cookie demo beside it. It issues opaque strings, signs nothing and asks nobody anything —
/// it exists so <c>dotnet run</c> and the e2e tier can complete a real redirect round trip
/// with no external service and no network (R27). Nothing here is an identity provider, and
/// nothing here is framework: the framework half is <c>libs/nativeweb.signin</c>, which is the
/// client and never the server.
/// </para>
/// <para>
/// The relying party runs alongside the sample's cookie scheme rather than replacing it. Two
/// schemes in one app is the configuration a product arrives at — the scaffolded admin keeps
/// challenging with its own — and it is the case that would break first if the library's keyed
/// registration were wrong.
/// </para>
/// <para>
/// Every response is written as a JSON string rather than through <c>Results.Json</c>. This app
/// publishes NativeAOT, so a serializer for an anonymous or reflected type is a trim warning
/// under repo-wide <c>TreatWarningsAsErrors</c>; four fixed-shape payloads are cheaper to write
/// out than a serializer context is to carry.
/// </para>
/// </remarks>
public sealed class DemoProvider
{
    private readonly ConcurrentDictionary<string, string> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _accessTokens = new(StringComparer.Ordinal);

    /// <summary>Where the demo provider's endpoints live.</summary>
    public const string BasePath = "/demo-provider";

    /// <summary>Who every demo sign-in is for.</summary>
    public const string Subject = "demo-subject";

    /// <summary>The address the demo provider reports for that subject.</summary>
    public const string Email = "demo@example.test";

    /// <summary>The origin this app is listening on, which its own client must call back to.</summary>
    /// <param name="configuration">The app's configuration, holding the bound URLs.</param>
    /// <returns>An absolute origin with no trailing slash.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    public static string SelfOrigin(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? urls = configuration["urls"] ?? configuration["ASPNETCORE_URLS"];

        string first = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? "http://localhost:5000";

        return first
            .Replace("*", "localhost", StringComparison.Ordinal)
            .Replace("+", "localhost", StringComparison.Ordinal)
            .TrimEnd('/');
    }

    /// <summary>
    /// The origin the relying party calls the provider on server-to-server, which is the browser's
    /// origin unless something says otherwise.
    /// </summary>
    /// <param name="configuration">The app's configuration.</param>
    /// <returns>An absolute origin with no trailing slash.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    /// <remarks>
    /// A real provider is a different host, so the front channel (the redirect the browser walks)
    /// and the back channel (token, userinfo, introspection) are separately addressable in any
    /// deployment. Here they are the same process, and are the same origin for anyone running
    /// <c>dotnet run</c> — the key exists because the e2e tier must serve the browser over https,
    /// for the <c>__Host-</c> cookie, while the app's own HttpClient has no reason to trust the
    /// certificate a test minted for the occasion. Splitting the two is what lets the browser have
    /// its secure origin without a test changing the machine's trust store.
    /// </remarks>
    public static string BackChannelOrigin(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration["Demo:BackChannelOrigin"] is { Length: > 0 } configured
            ? configured.TrimEnd('/')
            : SelfOrigin(configuration);
    }

    internal IResult Authorize(HttpContext http)
    {
        string state = http.Request.Query["state"].ToString();
        string redirect = http.Request.Query["redirect_uri"].ToString();

        string code = Guid.NewGuid().ToString("N");
        _codes[code] = http.Request.Query["code_challenge"].ToString();

        return Results.Redirect(
            $"{redirect}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");
    }

    internal async Task<IResult> TokenAsync(HttpContext http)
    {
        IFormCollection form = await http.Request.ReadFormAsync().ConfigureAwait(false);

        if (!_codes.TryRemove(form["code"].ToString(), out string? challenge)
            || Challenge(form["code_verifier"].ToString()) != challenge)
        {
            return Json("""{"error":"invalid_grant"}""", StatusCodes.Status400BadRequest);
        }

        string accessToken = Guid.NewGuid().ToString("N");
        _accessTokens[accessToken] = 0;

        return Json(
            $$"""{"access_token":"{{accessToken}}","token_type":"Bearer","expires_in":1800}""");
    }

    internal IResult UserInfo(HttpContext http) =>
        _accessTokens.ContainsKey(Bearer(http))
            ? Json($$"""{"sub":"{{Subject}}","email":"{{Email}}"}""")
            : Results.Unauthorized();

    internal async Task<IResult> IntrospectAsync(HttpContext http)
    {
        IFormCollection form = await http.Request.ReadFormAsync().ConfigureAwait(false);

        return Json(_accessTokens.ContainsKey(form["token"].ToString())
            ? $$"""{"active":true,"sub":"{{Subject}}"}"""
            : """{"active":false}""");
    }

    private static IResult Json(string body, int status = StatusCodes.Status200OK) =>
        Results.Content(body, "application/json", Encoding.UTF8, status);

    private static string Bearer(HttpContext http)
    {
        string header = http.Request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : "";
    }

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

/// <summary>
/// Mounts the demo provider, and the two pages the sign-in demo is for.
/// </summary>
internal static class DemoSignInEndpoints
{
    /// <summary>Maps the four endpoints the relying party calls.</summary>
    /// <param name="endpoints">The app's route builder.</param>
    /// <returns><paramref name="endpoints"/>, for chaining.</returns>
    public static IEndpointRouteBuilder MapDemoProvider(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet($"{DemoProvider.BasePath}/authorize", (HttpContext http, DemoProvider provider) =>
            provider.Authorize(http));

        endpoints.MapPost($"{DemoProvider.BasePath}/token", (HttpContext http, DemoProvider provider) =>
            provider.TokenAsync(http));

        endpoints.MapGet($"{DemoProvider.BasePath}/userinfo", (HttpContext http, DemoProvider provider) =>
            provider.UserInfo(http));

        endpoints.MapPost($"{DemoProvider.BasePath}/introspect", (HttpContext http, DemoProvider provider) =>
            provider.IntrospectAsync(http));

        return endpoints;
    }

    /// <summary>Maps the signed-in page and its signed-out counterpart.</summary>
    /// <param name="endpoints">The app's route builder.</param>
    /// <returns><paramref name="endpoints"/>, for chaining.</returns>
    public static IEndpointRouteBuilder MapDemoSignIn(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/account", (HttpContext http, IAntiforgery antiforgery) =>
        {
            string subject = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);
            HtmlEncoder encode = HtmlEncoder.Default;

            return Results.Content(
                $"""
                 <!doctype html><title>Account</title>
                 <h1>Signed in</h1>
                 <p id="subject">{encode.Encode(subject)}</p>
                 <form method="post" action="{DemoSignInPaths.Logout}">
                 <input type="hidden" name="{encode.Encode(tokens.FormFieldName!)}" value="{encode.Encode(tokens.RequestToken!)}">
                 <button type="submit" id="signout">Sign out</button>
                 </form>
                 """,
                "text/html");
        })
        .RequireAuthorization(policy => policy
            .AddAuthenticationSchemes(SessionAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser());

        // The signed-out side of R28. /articles is public as well, but it is public because
        // nobody thought about it; this one is public next to a page that is not.
        endpoints.MapGet("/welcome", () => Results.Content(
            $"""
             <!doctype html><title>Welcome</title>
             <h1 id="welcome">Anyone may read this</h1>
             <p><a href="/account" id="to-account">Account</a></p>
             <p><a href="{DemoSignInPaths.Login}" id="to-signin">Sign in</a></p>
             """,
            "text/html"));

        return endpoints;
    }
}

/// <summary>
/// The relying party's three paths in this app.
/// </summary>
/// <remarks>
/// Not the library's defaults, which start <c>/signin</c>: this app already has a
/// <c>/signin</c> belonging to the cookie demo, and two schemes cannot both own a path.
/// </remarks>
internal static class DemoSignInPaths
{
    /// <summary>
    /// The scheme that decides which of the app's two schemes answers a request that named
    /// neither.
    /// </summary>
    /// <remarks>
    /// Without it, everything the relying party signs in is anonymous to every middleware that
    /// reads <c>HttpContext.User</c> — because <c>UseAuthentication</c> runs the default scheme,
    /// and the default scheme is the cookie demo's. Antiforgery is the one that bites first: the
    /// token on the signed-in page is bound to the visitor's identity, the POST that carries it
    /// back is seen as anonymous, and the sign-out form is refused with a 400 that says nothing
    /// about schemes. A product mounting sign-in beside an existing scheme needs this rule; the
    /// library cannot supply it, because which scheme wins is the product's decision.
    /// </remarks>
    internal const string EitherScheme = "sample-either";

    /// <summary>The session cookie's name, named here because the selector above reads it.</summary>
    internal const string SessionCookie = "__Host-signin";

    internal const string Login = "/oauth/signin";

    internal const string Callback = "/oauth/signin/callback";

    internal const string Logout = "/oauth/signout";
}
