using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Xunit;

namespace NativeWeb.Permissions.Tests.Unit;

/// <summary>
/// <c>UseAntiforgery</c> validates a request only where the endpoint carries
/// <see cref="IAntiforgeryMetadata"/>, so a screen whose posts lack it renders a
/// token nothing checks and answers a tokenless post as if it were legitimate.
/// </summary>
/// <remarks>
/// The metadata, not the refusal: turning a failed validation into a 400 is
/// <c>AntiforgeryValidationMiddleware</c>'s job in <c>libs/nativeweb.web</c>, which
/// this library does not reference. The refusal itself is proved through
/// <c>apps/sample</c>'s real pipeline, in the scaffold integration tier.
/// </remarks>
public sealed class AccessAdminAntiforgeryTests
{
    private static readonly Permission View = new("sample:article.view");

    [Fact]
    public async Task EveryPostTheScreensMap_CarriesTheMetadata()
    {
        await using Harness harness = await Harness.StartAsync();

        RouteEndpoint[] posts =
        [
            .. harness.Endpoints
                .OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/admin/access", StringComparison.Ordinal)
                    == true)
                .Where(endpoint =>
                    endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") == true),
        ];

        Assert.Equal(7, posts.Length);
        Assert.All(posts, post => Assert.NotNull(post.Metadata.GetMetadata<IAntiforgeryMetadata>()));
    }

    [Fact]
    public void EveryGetTheScreensMap_CarriesNone()
    {
        // Not symmetry for its own sake: the metadata makes UseAntiforgery mandatory
        // for the host, and a GET that demanded a token would be unreachable.
        using var app = new HostAssertion();

        Assert.All(
            app.Gets,
            get => Assert.Null(get.Metadata.GetMetadata<IAntiforgeryMetadata>()));
    }

    private sealed class HostAssertion : IDisposable
    {
        private readonly WebApplication _app;

        internal HostAssertion()
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IPermissionStore>(new InMemoryPermissionStore());
            builder.Services.AddPermissions([View]);
            builder.Services.AddAntiforgery();

            _app = builder.Build();
            _app.UseAntiforgery();
            _app.MapAccessAdmin(
                "/admin",
                (http, title, body) => Results.Content(body, "text/html"),
                _ => string.Empty);
        }

        internal IEnumerable<RouteEndpoint> Gets =>
            _app.Services.GetRequiredService<EndpointDataSource>()
                .Endpoints
                .OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/admin/access", StringComparison.Ordinal)
                    == true)
                .Where(endpoint =>
                    endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);

        public void Dispose() => ((IDisposable)_app).Dispose();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private WebApplication _app = null!;

        internal IReadOnlyList<Endpoint> Endpoints =>
            _app.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        internal static async Task<Harness> StartAsync()
        {
            var harness = new Harness();

            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IPermissionStore>(new InMemoryPermissionStore());
            builder.Services.AddPermissions([View]);
            builder.Services.AddAntiforgery();

            harness._app = builder.Build();
            harness._app.UseAntiforgery();

            harness._app.MapAccessAdmin(
                "/admin",
                (http, title, body) => Results.Content(body, "text/html"),
                _ => string.Empty);

            await harness._app.StartAsync();

            return harness;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
