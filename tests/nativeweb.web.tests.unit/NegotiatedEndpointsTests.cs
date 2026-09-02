using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

using NativeWeb.Render;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

/// <summary>
/// <see cref="NegotiatedEndpoints.MapNegotiated"/> through a real host: two
/// routes, one handler, the refusals answered before the handler runs.
/// </summary>
public sealed partial class NegotiatedEndpointsTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private static readonly string[] Ids = ["a1"];
    private int _handlerRuns;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        _app = builder.Build();

        _app.MapNegotiated("/articles", [Format.Html, Format.Json, Format.Csv], (http, format) =>
        {
            Interlocked.Increment(ref _handlerRuns);
            var parked = (Format)http.Items[Negotiation.ItemKey]!;
            Assert.Equal(format, parked);

            return ValueTask.FromResult(format == Format.Json
                ? FormatResults.Json(Ids, TestJson.Default.StringArray)
                : format == Format.Csv
                    ? FormatResults.Csv(["id"], [["a1"]])
                    : Results.Content("<p>a1</p>", "text/html; charset=utf-8"));
        }).WithMetadata(new Marker());

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string? accept = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);

        if (accept is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task ThePlainRouteAnswersTheDefaultAndVariesOnAccept()
    {
        using HttpResponseMessage response = await GetAsync("/articles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Accept", response.Headers.Vary);
        Assert.Equal("<p>a1</p>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheExtensionRouteAnswersThatFormatWithoutVary()
    {
        using HttpResponseMessage response = await GetAsync("/articles.json", accept: "text/html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(response.Headers.Vary);
        Assert.Equal("[\"a1\"]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnAcceptHeaderPicksTheFormatOnThePlainRoute()
    {
        using HttpResponseMessage response = await GetAsync("/articles", accept: "text/html;q=0.5, text/csv;q=0.9");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Accept", response.Headers.Vary);
        Assert.Equal("id\r\na1\r\n", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/articles.xml")]
    [InlineData("/articles.a.b")]
    [InlineData("/articles.exe")]
    public async Task AnUnofferedExtensionIs404NamingTheOffersBeforeTheHandlerRuns(string path)
    {
        int before = _handlerRuns;

        using HttpResponseMessage response = await GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Offered: text/html, application/json, text/csv\n", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, _handlerRuns);
    }

    [Fact]
    public async Task AnAcceptHeaderAdmittingNothingIs406NamingTheOffersBeforeTheHandlerRuns()
    {
        int before = _handlerRuns;

        using HttpResponseMessage response = await GetAsync("/articles", accept: "image/png");

        Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Offered: text/html, application/json, text/csv\n", await response.Content.ReadAsStringAsync());
        Assert.Contains("Accept", response.Headers.Vary);
        Assert.Equal(before, _handlerRuns);
    }

    [Theory]
    [InlineData("/articles", "text/html")]
    [InlineData("/articles.csv", "text/csv")]
    public async Task HeadIsAnsweredWithTheSameHeadersAsGet(string path, string mediaType)
    {
        using HttpRequestMessage head = new(HttpMethod.Head, path);
        using HttpResponseMessage headResponse = await _client.SendAsync(head);
        using HttpResponseMessage getResponse = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);
        Assert.Equal(mediaType, headResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(getResponse.Content.Headers.ContentLength, headResponse.Content.Headers.ContentLength);
    }

    [Fact]
    public void AConventionLandsOnBothRoutes()
    {
        EndpointDataSource source = _app.Services.GetRequiredService<EndpointDataSource>();
        RouteEndpoint[] marked = source.Endpoints
            .OfType<RouteEndpoint>()
            .Where(static e => e.Metadata.GetMetadata<Marker>() is not null)
            .ToArray();

        Assert.Equal(2, marked.Length);
        Assert.Contains(marked, static e => e.RoutePattern.RawText == "/articles");
        Assert.Contains(marked, static e => e.RoutePattern.RawText == "/articles.{format}");
        Assert.All(marked, static e => Assert.NotNull(e.Metadata.GetMetadata<NegotiationMetadata>()));
    }

    [Theory]
    [InlineData("articles")]
    [InlineData("/articles/")]
    [InlineData("/articles/{id}")]
    public void ARouteMustBeAPlainPath(string pattern)
    {
        WebApplication app = WebApplication.CreateSlimBuilder().Build();

        Assert.Throws<ArgumentException>(() =>
            app.MapNegotiated(pattern, [Format.Html], static (_, _) => ValueTask.FromResult(Results.Ok())));
    }

    [Fact]
    public void AFormatIsOfferedAtMostOnce()
    {
        WebApplication app = WebApplication.CreateSlimBuilder().Build();

        Assert.Throws<ArgumentException>(() =>
            app.MapNegotiated("/x", [Format.Html, Format.Html], static (_, _) => ValueTask.FromResult(Results.Ok())));
    }

    private sealed class Marker
    {
    }

    [JsonSerializable(typeof(string[]))]
    private sealed partial class TestJson : JsonSerializerContext
    {
    }
}
