using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;

using NativeWeb.Forms;
using NativeWeb.Render;
using NativeWeb.Web;

namespace NativeWeb.Sample;

/// <summary>
/// What the sign-in form posts: the passphrase and where to go afterwards.
/// </summary>
/// <param name="Passphrase">What the visitor typed.</param>
/// <param name="ReturnUrl">The page that redirected here, carried as a hidden field named <c>return</c>.</param>
[Form]
public sealed partial record SignInForm(
    string? Passphrase,
    [FormField("return")] string? ReturnUrl);

/// <summary>
/// The one secret that admits an operator to the deployed sample's admin (spec 020).
/// </summary>
/// <remarks>
/// <para>
/// Both sides are hashed before comparison, so the comparison is over two 32-byte
/// digests whatever the lengths involved and <see cref="CryptographicOperations.FixedTimeEquals"/>
/// can do its job: neither the secret's length nor how far a guess got shapes the
/// timing. An unconfigured secret compares against a zero digest and then refuses, so
/// the refusal path costs the same as every other.
/// </para>
/// <para>
/// This is the sample's, not the framework's. A single shared passphrase in front of
/// one product's admin is that product's decision; the framework's sign-in is the
/// OAuth relying party in <c>libs/nativeweb.signin</c>, which the skeleton uses.
/// </para>
/// </remarks>
public sealed class AdminPassphrase
{
    /// <summary>The key under the product's section: <c>{Section}:Admin:Passphrase</c>.</summary>
    public const string Key = "Admin:Passphrase";

    private static readonly byte[] Unset = new byte[32];

    private readonly byte[]? _digest;

    /// <summary>Wraps a secret, or nothing when the value is empty.</summary>
    /// <param name="secret">The configured value, or null.</param>
    public AdminPassphrase(string? secret)
    {
        _digest = string.IsNullOrWhiteSpace(secret) ? null : SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>Whether a secret is configured at all.</summary>
    public bool IsConfigured => _digest is not null;

    /// <summary>
    /// Whether <paramref name="candidate"/> is the secret. Always false when none is
    /// configured, and takes the same time either way.
    /// </summary>
    /// <param name="candidate">What the visitor typed, or null.</param>
    /// <returns>True only for the configured secret.</returns>
    public bool Matches(string? candidate)
    {
        byte[] offered = SHA256.HashData(Encoding.UTF8.GetBytes(candidate ?? ""));
        bool equal = CryptographicOperations.FixedTimeEquals(offered, _digest ?? Unset);

        return equal && _digest is not null;
    }

    /// <summary>
    /// Reads the secret from <c>{section}:Admin:Passphrase</c>. In Production a missing
    /// value fails startup naming the key; anywhere else it is carried as unset and every
    /// attempt is refused.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="section">The product's section name, <c>Sample</c> here.</param>
    /// <param name="environment">The host environment.</param>
    /// <returns>The wrapped secret.</returns>
    public static AdminPassphrase FromConfiguration(
        IConfiguration configuration, string section, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(environment);

        string key = $"{section}:{Key}";
        string? value = configuration[key];

        if (environment.IsProduction() && string.IsNullOrWhiteSpace(value))
        {
            StartupConfigGuards.FailStartup(
                $"{key} is not set. Outside Development the admin is reached only through the passphrase "
                + "form at /signin, and Production refuses to start without one; on a deployed host it "
                + "lives in /etc/nativeweb/secrets.env. provision.sh seeds the line only into a file it "
                + "creates, so a host provisioned before the key existed needs it added by hand.");
        }

        return new AdminPassphrase(value);
    }
}

/// <summary>
/// The passphrase sign-in every environment but Development mounts at <c>/signin</c>.
/// </summary>
public static class AdminSignIn
{
    /// <summary>The rate-limit policy on the POST, stricter than the framework's general one.</summary>
    public const string RateLimitPolicy = "signin";

    /// <summary>Attempts per client address per minute before the POST answers 429.</summary>
    public const int PermitLimit = 10;

    private const string DefaultReturn = "/admin";

    /// <summary>Registers the <see cref="RateLimitPolicy"/> beside the framework's policies.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddAdminSignInRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddRateLimiter(limiter =>
            limiter.AddPolicy(RateLimitPolicy, http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: NativeWebPipeline.ClientIpKey(http),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromSeconds(60),
                        PermitLimit = PermitLimit,
                        QueueLimit = 0,
                    })));
    }

    /// <summary>
    /// Maps <c>GET /signin</c> (the form, or the signed-in state with a sign-out button),
    /// <c>POST /signin</c> and <c>POST /signout</c>.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="passphrase">The configured secret.</param>
    /// <returns>The same builder.</returns>
    public static IEndpointRouteBuilder MapAdminSignIn(this IEndpointRouteBuilder endpoints, AdminPassphrase passphrase)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(passphrase);

        endpoints.MapGet("/signin", (HttpContext http) =>
            Page(http, refused: false, LocalPath(http.Request.Query["ReturnUrl"].ToString())));

        endpoints.MapFormPost<SignInForm>("/signin", async (form, http) =>
        {
            if (!passphrase.Matches(form.Passphrase))
            {
                return Page(http, refused: true, LocalPath(form.ReturnUrl));
            }

            // NameIdentifier is what the permission layer reads (spec 024 R17): it
            // knows nothing about how anyone signed in, only that a request carries
            // an opaque subject. `operator` is the subject migration 0006 seeds the
            // first grants against, so the two have to agree or the access admin is
            // unreachable on a fresh database.
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, "operator"),
                    new Claim(ClaimTypes.NameIdentifier, "operator"),
                    new Claim(ClaimTypes.Role, "admin"),
                ],
                CookieAuthenticationDefaults.AuthenticationScheme);

            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity)).ConfigureAwait(false);

            return Results.LocalRedirect(LocalPath(form.ReturnUrl));
        }).RequireRateLimiting(RateLimitPolicy);

        endpoints.MapPost("/signout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
            return Results.Redirect("/");
        }).RequireFormAntiforgery();

        return endpoints;
    }

    // A path on this host, or /admin. Anything with a scheme or a protocol-relative
    // prefix is an open redirect and is dropped, not escaped. So is anything holding a
    // control character: a browser strips a tab or a newline before it parses the
    // Location, which turns "/<tab>/evil" into a protocol-relative URL after the
    // check above has passed it.
    internal static string LocalPath(string? candidate) =>
        candidate is { Length: > 1 }
        && candidate[0] == '/'
        && candidate[1] != '/'
        && candidate[1] != '\\'
        && !candidate.Any(char.IsControl)
            ? candidate
            : DefaultReturn;

    // One page for every state. A refusal is the same page with the message shown and a
    // 401, and it is rendered the same way whatever was typed (spec 020 R3) -- including
    // by a visitor who already holds a session, who otherwise saw the signed-in branch
    // with no message and no form.
    private static IResult Page(HttpContext http, bool refused, string returnUrl)
    {
        IAntiforgery antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);
        LocaleCatalog locale = Locales.Get(http.GetLocale());

        string html = Templates.Signin.Form.ToHtmlString(
            locale: locale,
            Title: locale["signin.title"],
            AntiforgeryToken: tokens.RequestToken ?? "",
            FlashOk: "",
            FlashError: "",
            AntiforgeryFieldName: tokens.FormFieldName,
            SignedInAs: !refused && http.User.Identity?.IsAuthenticated == true ? http.User.Identity.Name ?? "" : "",
            Refused: refused,
            ReturnUrl: returnUrl);

        return Results.Content(
            html,
            "text/html",
            Encoding.UTF8,
            refused ? StatusCodes.Status401Unauthorized : StatusCodes.Status200OK);
    }
}
