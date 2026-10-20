using System.Globalization;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.TestHost;

using NativeWeb.Render;

using __PRODUCT__;

namespace NativeWeb.Web.Tests.Unit;

/// <summary>
/// The spec-006 triangle, composed. Not a copy of the skeleton's
/// <c>Program.cs</c> but the slice of it the invariants are about: the security
/// headers, locale negotiation, the rate limiter, the output cache with
/// <see cref="PublicPageCachePolicy"/>, antiforgery, and one route declared with
/// <c>AsPublicPage</c> whose handler picks <see cref="Chrome.Anonymous"/> or
/// <see cref="Chrome.For"/> by the same cookie test the cache policy uses.
///
/// The handler renders a per-render counter into the body. Everything here
/// turns on telling a fresh render from a replay, and two responses being equal
/// cannot say which happened — a cache hit and a byte-stable render look
/// identical from outside. The counter makes the difference visible, so
/// <see cref="Renders"/> is what most of these assertions are really about.
///
/// <see cref="WithCacheAsync"/> and <see cref="WithoutCacheAsync"/> exist for
/// the same reason. Byte-stability is a property of the <b>render</b>, and
/// asserting it with the cache in front would prove only that the store returns
/// what it was handed. So that one fact is measured with the cache out of the
/// pipeline, where a second identical body means a second render produced it.
/// </summary>
internal sealed class PublicPagePipeline : IAsyncDisposable
{
    /// <summary>
    /// The second catalog under <c>locales/</c>. Two are needed because one
    /// cannot show that locale separates cache entries, and the skeleton this
    /// tier compiles ships only <c>en</c>.
    /// </summary>
    public const string OtherLocale = "fr";

    private readonly WebApplication _app;
    private readonly RenderCounter _counter;

    private PublicPagePipeline(WebApplication app, RenderCounter counter, HttpClient client)
    {
        _app = app;
        _counter = counter;
        Client = client;
    }

    /// <summary>How many times the endpoint handler has run.</summary>
    public int Renders => _counter.Value;

    public HttpClient Client { get; }

    /// <summary>The full pipeline, on a route declared publicly cacheable.</summary>
    public static Task<PublicPagePipeline> WithCacheAsync() => StartAsync(publicPage: true);

    /// <summary>
    /// The same pipeline on a route that never says <c>AsPublicPage</c>. Nothing
    /// is cached by default — the named policy is the only one registered — so
    /// this is the real seam with the store out of the picture rather than a
    /// second, hand-built composition.
    /// </summary>
    public static Task<PublicPagePipeline> WithoutCacheAsync() => StartAsync(publicPage: false);

    /// <summary>
    /// The full pipeline with a limiter that permits <paramref name="permitLimit"/>
    /// requests a minute and refuses the rest. Spec 006 R8's ordering is only
    /// observable through a limiter that can be exhausted: a cache hit must
    /// still spend a permit, and the only way to see whether it did is to run
    /// out.
    /// </summary>
    public static Task<PublicPagePipeline> WithRateLimitAsync(int permitLimit) =>
        StartAsync(publicPage: true, permitLimit: permitLimit);

    private static async Task<PublicPagePipeline> StartAsync(bool publicPage, int? permitLimit = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            // The error shell and the clean-URL route map are both read from the
            // web root at startup, so this tier ships the one file they need.
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = "wwwroot",
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        NativeWebOptions options = new()
        {
            ApplicationName = "Demo",
            ConfigurationSection = "Demo",
            TelemetryServiceName = "Demo",
            // Under bin/, because a key ring in the user profile would make one
            // test run visible to the next.
            DataProtectionKeyDirectory = Path.Combine(AppContext.BaseDirectory, "dp-keys"),
            UploadDirectory = Path.Combine(AppContext.BaseDirectory, "uploads"),
            // A limiter no test can reach unless a test asks for one it can. The
            // limiter is in the pipeline regardless, because its position
            // relative to the cache is itself an invariant (spec 006 R8).
            GeneralPermitLimit = permitLimit ?? 1_000_000,
        };

        builder.AddNativeWebWeb(options);

        WebApplication app = builder.Build();
        app.UseNativeWebPipeline(Locales.All);

        AssetUrls assets = new(app);
        IAntiforgery antiforgery = app.Services.GetRequiredService<IAntiforgery>();
        RenderCounter counter = new();

        RouteHandlerBuilder route = app.MapGet("/", IResult (HttpContext context) =>
        {
            int render = counter.Next();

            // The same branch the real pages take: a cookie-free request gets
            // the byte-stable anonymous chrome, anything carrying one gets the
            // full chrome, with a token and a canary in it.
            Chrome chrome = PublicPageCache.CarriesIdentityCookie(context.Request)
                ? Chrome.For(context, assets, antiforgery, Locales.Get(context.GetLocale()))
                : Chrome.Anonymous(context, assets, Locales.Get(context.GetLocale()));

            // Rendered by hand rather than through a template so the body is a
            // flat statement of the five fields the invariants are about, and a
            // change to any one of them shows up as a body difference.
            string body = string.Create(
                CultureInfo.InvariantCulture,
                $"""
                 <p id="render">{render}</p>
                 <p id="locale">{chrome.Locale["chrome.skip"]}</p>
                 <p id="token">{chrome.AntiforgeryToken}</p>
                 <p id="nonce">{chrome.Nonce}</p>
                 <p id="canary">{chrome.BotCanary}</p>
                 """);

            return Results.Content(body, "text/html; charset=utf-8");
        })
        .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy);

        if (publicPage)
        {
            route.AsPublicPage(PublicPageCache.Csp([]));
        }

        await app.StartAsync();

        HttpClient client = app.GetTestClient();
        // https, because the antiforgery cookie is configured SecurePolicy.Always
        // as the skeleton ships it, and GetAndStoreTokens throws over plain http.
        // Weakening the policy in the fixture would test a configuration no
        // product runs.
        client.BaseAddress = new Uri("https://localhost/");
        return new PublicPagePipeline(app, counter, client);
    }

    /// <summary>
    /// A GET carrying whatever headers and cookies the caller names. Returns the
    /// response and its body together, because every assertion here wants both.
    /// </summary>
    public async Task<(HttpResponseMessage Response, string Body)> GetAsync(
        string path = "/", string? acceptLanguage = null, string? cookie = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        if (acceptLanguage is not null)
        {
            request.Headers.Add("Accept-Language", acceptLanguage);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        HttpResponseMessage response = await Client.SendAsync(request);
        return (response, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A cookie header carrying the one cookie that personalizes a page.</summary>
    public static string IdentityCookie(string value = "iok") =>
        $"{Flash.CookieName}={value}";

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private sealed class RenderCounter
    {
        private int _value;

        public int Value => Volatile.Read(ref _value);

        public int Next() => Interlocked.Increment(ref _value);
    }
}
