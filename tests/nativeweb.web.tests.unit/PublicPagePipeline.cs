using System.Globalization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;

using NativeWeb.Render;

using __PRODUCT__;
using __PRODUCT__.Middleware;
using __PRODUCT__.Render;

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

    /// <summary>The full pipeline, output cache included.</summary>
    public static Task<PublicPagePipeline> WithCacheAsync() => StartAsync(outputCache: true);

    /// <summary>The same pipeline with <c>UseOutputCache</c> omitted.</summary>
    public static Task<PublicPagePipeline> WithoutCacheAsync() => StartAsync(outputCache: false);

    /// <summary>
    /// The full pipeline with a limiter that permits <paramref name="permitLimit"/>
    /// requests and refuses the rest. Spec 006 R8's ordering is only observable
    /// through a limiter that can be exhausted: a cache hit must still spend a
    /// permit, and the only way to see whether it did is to run out.
    /// </summary>
    public static Task<PublicPagePipeline> WithRateLimitAsync(int permitLimit) =>
        StartAsync(outputCache: true, permitLimit: permitLimit);

    private static async Task<PublicPagePipeline> StartAsync(bool outputCache, int? permitLimit = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddRouting();
        builder.Services.AddOutputCache(options =>
            options.AddPolicy(PublicPageCachePolicy.Name, policy => policy.AddPolicy<PublicPageCachePolicy>()));

        // Unbounded unless a test asks otherwise. The limiter is in the pipeline
        // regardless, because its position relative to the cache is itself an
        // invariant (spec 006 R8) and not every test wants to be throttled to
        // observe it.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("general", _ => permitLimit is int limit
                ? RateLimitPartition.GetFixedWindowLimiter("all", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    // Longer than any test run, so exhaustion is permanent within
                    // one pipeline rather than a race against the clock.
                    Window = TimeSpan.FromMinutes(10),
                    QueueLimit = 0,
                })
                : RateLimitPartition.GetNoLimiter("all"));
        });

        builder.Services.AddAntiforgery(static options =>
        {
            options.Cookie.Name = "__Host-nw-test-antiforgery";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
        });

        WebApplication app = builder.Build();

        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseRouting();
        app.UseLocaleNegotiation(new LocaleNegotiationOptions { SupportedLocales = Locales.All });
        app.UseRateLimiter();
        if (outputCache)
        {
            app.UseOutputCache();
        }

        app.UseAntiforgery();

        AssetUrls assets = new(app);
        IAntiforgery antiforgery = app.Services.GetRequiredService<IAntiforgery>();
        RenderCounter counter = new();

        app.MapGet("/", IResult (HttpContext context) =>
        {
            int render = counter.Next();

            // The same branch the real pages take: a cookie-free request gets
            // the byte-stable anonymous chrome, anything carrying one gets the
            // full chrome, with a token and a canary in it.
            Chrome chrome = PublicPageCache.CarriesIdentityCookie(context.Request)
                ? Chrome.For(context, assets, antiforgery)
                : Chrome.Anonymous(context, assets);

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
        .RequireRateLimiting("general")
        .AsPublicPage(PublicPageCache.Csp([]));

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
