using System.Globalization;
using System.Net;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

using NativeWeb.Render;

using __PRODUCT__;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

/// <summary>
/// Spec 021 R7 on the framework's own pipeline: a negotiated route under the
/// public-page policy is cached per representation, and a body of the wrong
/// type is never stored under a key that promises another.
/// </summary>
/// <remarks>
/// The handler renders a counter into every body, for the reason
/// <see cref="PublicPagePipeline"/> gives: a cache hit and a byte-stable render
/// look the same from outside, and the counter is what tells them apart.
/// The route offers two cookie-free formats and, when asked, deliberately
/// answers JSON with a text body so the storage refusal can be seen.
/// </remarks>
public sealed class NegotiatedCacheTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private int _renders;
    private bool _lie;

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = "wwwroot",
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.AddNativeWebWeb(new NativeWebOptions
        {
            ApplicationName = "Demo",
            ConfigurationSection = "Demo",
            TelemetryServiceName = "Demo",
            DataProtectionKeyDirectory = Path.Combine(AppContext.BaseDirectory, "dp-keys"),
            UploadDirectory = Path.Combine(AppContext.BaseDirectory, "uploads"),
            GeneralPermitLimit = 1_000_000,
        });

        _app = builder.Build();
        _app.UseNativeWebPipeline(Locales.All);

        _app.MapNegotiated("/doc", [Format.Text, Format.Json], (http, format) =>
        {
            int render = Interlocked.Increment(ref _renders);
            string body = string.Create(CultureInfo.InvariantCulture, $"render {render} {http.GetLocale()}");

            return ValueTask.FromResult(format == Format.Json && !_lie
                ? Results.Content("{\"render\":" + render.ToString(CultureInfo.InvariantCulture) + "}", "application/json; charset=utf-8")
                : FormatResults.Text(body));
        })
        .RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy)
        .AsPublicPage(PublicPageCache.Csp([]));

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("https://localhost/");
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task<(HttpResponseMessage Response, string Body)> GetAsync(
        string path,
        string? accept = null,
        string? language = null,
        string? authorization = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);

        if (accept is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        if (language is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", language);
        }

        HttpResponseMessage response = await _client.SendAsync(request);
        return (response, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EachRepresentationIsCachedUnderItsOwnKey()
    {
        (HttpResponseMessage text1, string textBody1) = await GetAsync("/doc", accept: "text/plain");
        (HttpResponseMessage json1, string jsonBody1) = await GetAsync("/doc", accept: "application/json");
        (HttpResponseMessage text2, string textBody2) = await GetAsync("/doc", accept: "text/plain");
        (HttpResponseMessage json2, string jsonBody2) = await GetAsync("/doc", accept: "application/json");

        Assert.Equal(HttpStatusCode.OK, text1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, json1.StatusCode);
        Assert.Equal("text/plain", text1.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/json", json1.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/json", json2.Content.Headers.ContentType?.MediaType);
        Assert.Equal(textBody1, textBody2);
        Assert.Equal(jsonBody1, jsonBody2);
        Assert.NotEqual(textBody1, jsonBody1);
        Assert.Equal(2, _renders);
        Assert.Contains("Accept", text2.Headers.Vary);
        Assert.Contains("Accept", json2.Headers.Vary);
    }

    [Fact]
    public async Task TheExtensionRouteIsItsOwnKeyToo()
    {
        (_, string a) = await GetAsync("/doc.json");
        (_, string b) = await GetAsync("/doc.json");
        (HttpResponseMessage viaAccept, string c) = await GetAsync("/doc", accept: "application/json");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal("application/json", viaAccept.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, _renders);
    }

    [Fact]
    public async Task LocaleStillSeparatesEntries()
    {
        (_, string en) = await GetAsync("/doc", accept: "text/plain", language: "en");
        (_, string fr) = await GetAsync("/doc", accept: "text/plain", language: PublicPagePipeline.OtherLocale);
        (_, string enAgain) = await GetAsync("/doc", accept: "text/plain", language: "en");

        Assert.NotEqual(en, fr);
        Assert.Equal(en, enAgain);
        Assert.Equal(2, _renders);
    }

    [Fact]
    public async Task ARequestCarryingAuthorizationNeitherReadsNorFillsTheSharedStore()
    {
        (_, string bearer1) = await GetAsync("/doc", accept: "text/plain", authorization: "Bearer x");
        (_, string bearer2) = await GetAsync("/doc", accept: "text/plain", authorization: "Bearer x");
        (_, string anonymous1) = await GetAsync("/doc", accept: "text/plain");
        (_, string anonymous2) = await GetAsync("/doc", accept: "text/plain");

        Assert.NotEqual(bearer1, bearer2);
        Assert.NotEqual(bearer2, anonymous1);
        Assert.Equal(anonymous1, anonymous2);
        Assert.Equal(3, _renders);
    }

    [Fact]
    public async Task ABodyOfAnotherTypeIsNotStoredUnderTheChosenFormat()
    {
        _lie = true;
        (HttpResponseMessage first, string a) = await GetAsync("/doc", accept: "application/json");
        (HttpResponseMessage second, string b) = await GetAsync("/doc", accept: "application/json");

        Assert.Equal("text/plain", first.Content.Headers.ContentType?.MediaType);
        Assert.Equal("text/plain", second.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual(a, b);
        Assert.Equal(2, _renders);
    }
}
