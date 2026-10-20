using System.Security.Cryptography;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NativeWeb.SignIn.Tests.Integration;

/// <summary>
/// A real host with the library's endpoints on it, and the stub provider mounted alongside.
/// </summary>
/// <remarks>
/// One host serves both halves, and the library's own <see cref="HttpClient"/> for the scheme is
/// pointed back at that host's test handler. Every provider call is therefore an in-process call
/// through the same routing stack the endpoints under test use, and no socket is opened (R27).
/// </remarks>
public sealed class SignInHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly LoopbackHandler _loopback;

    private SignInHost(IHost host, LoopbackHandler loopback, StubProvider provider)
    {
        _host = host;
        _loopback = loopback;
        Provider = provider;
    }

    /// <summary>The authorization server the relying party is configured against.</summary>
    public StubProvider Provider { get; }

    /// <summary>The host's services, for <c>TestSession</c> and direct assertions.</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>A client that does not follow redirects and does not keep cookies for you.</summary>
    /// <returns>A client speaking to the host in process.</returns>
    public HttpClient Client() =>
        new(_host.GetTestServer().CreateHandler()) { BaseAddress = new Uri("http://localhost/") };

    /// <summary>Starts a host.</summary>
    /// <param name="configure">Extra registration, for the seams a case needs.</param>
    /// <returns>The started host.</returns>
    public static async Task<SignInHost> StartAsync(Action<SignInBuilder>? configure = null)
    {
        var provider = new StubProvider();
        var loopback = new LoopbackHandler();

        IHost host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
                    services.AddRouting();
                    services.AddSingleton(provider);

                    SignInBuilder builder = services.AddSignIn(options =>
                    {
                        options.AuthorizationEndpoint = "http://localhost/stub/authorize";
                        options.TokenEndpoint = "http://localhost/stub/token";
                        options.UserInfoEndpoint = "http://localhost/stub/userinfo";
                        options.IntrospectionEndpoint = "http://localhost/stub/introspect";
                        options.ClientId = "integration-client";
                        options.ClientSecret = "integration-secret";
                        options.RedirectUri = "http://localhost/signin/callback";
                        options.StoreEncryptionKey = RandomNumberGenerator.GetBytes(32);
                    });

                    configure?.Invoke(builder);

                    // Every server-to-server call the library makes goes back into this host.
                    services.AddHttpClient(builder.Scheme)
                        .ConfigurePrimaryHttpMessageHandler(() => loopback);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapSignIn();
                        provider.MapStub(endpoints);

                        endpoints.MapGet("/", () => Results.Text("public"));

                        endpoints.MapGet("/private", (HttpContext http) => Results.Text(
                            "private:" + (http.User.Identity?.Name
                                ?? http.User.FindFirst(
                                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                ?? "")))
                            .RequireAuthorization();

                        endpoints.MapGet("/api/whoami", (HttpContext http) => Results.Text("api"))
                            .RequireBearerToken();

                        // ISessionTokens injected by type and named by nobody, which is the whole
                        // point of the unkeyed registration for the first scheme.
                        endpoints.MapGet("/token-holder", async (
                            HttpContext http, ISessionTokens tokens, CancellationToken ct) =>
                            Results.Text(await tokens.GetAccessTokenAsync(http, ct).ConfigureAwait(false)
                                ?? "none"));

                        // The sign-out form's token pair, so a case can post a valid one.
                        endpoints.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) =>
                            Results.Text(antiforgery.GetAndStoreTokens(http).RequestToken ?? ""));
                    });
                }))
            .StartAsync()
            .ConfigureAwait(false);

        loopback.Target = host.GetTestServer().CreateHandler();

        return new SignInHost(host, loopback, provider);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync().ConfigureAwait(false);
        _host.Dispose();
        _loopback.Dispose();
    }
}

/// <summary>
/// Forwards the library's outbound requests to a handler supplied after the host starts.
/// </summary>
/// <remarks>
/// The indirection exists because the test server's handler does not exist until the host it
/// belongs to is running, and the client that must use it is registered before that.
/// </remarks>
internal sealed class LoopbackHandler : HttpMessageHandler
{
    private HttpMessageInvoker? _invoker;

    internal HttpMessageHandler? Target { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _invoker ??= new HttpMessageInvoker(
            Target ?? throw new InvalidOperationException("The host has not started yet."));

        return _invoker.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _invoker?.Dispose();
        }

        base.Dispose(disposing);
    }
}
