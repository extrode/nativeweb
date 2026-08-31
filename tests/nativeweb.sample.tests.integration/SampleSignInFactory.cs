using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using NativeWeb.SignIn;

namespace NativeWeb.Sample.Tests.Integration;

/// <summary>
/// A fresh <c>apps/sample</c> host per test, with the relying party's outbound calls
/// routed back into that same host.
/// </summary>
/// <remarks>
/// <para>
/// The sample's demo provider is mounted on the app itself, so in production the client
/// half reaches it over the loopback socket. A <c>TestServer</c> has no socket, so the
/// named client for the sign-in scheme gets a handler that forwards into this factory's
/// own server instead. Nothing about the app changes; only how its outbound request finds
/// the listener it was already addressing.
/// </para>
/// <para>
/// https, not http. The session cookie carries the <c>__Host-</c> prefix and is therefore
/// <c>Secure</c>, and <see cref="System.Net.CookieContainer"/> will not send a Secure
/// cookie back over an insecure scheme — a whole signed-in suite would pass its first
/// request and be anonymous from then on.
/// </para>
/// </remarks>
internal sealed class SampleSignInFactory : WebApplicationFactory<NativeWeb.Sample.Program>
{
    internal const string Origin = "https://localhost";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The whole sign-in demo, the demo provider included, is Development-gated.
        builder.UseEnvironment("Development");

        // What the app believes its own origin is, which is where it points its client
        // and what it registers as the redirect URI.
        builder.UseSetting("urls", Origin);

        // Force the in-memory store rather than relying on the key being absent: CI
        // exports ConnectionStrings__Sample for the JauntyQ tiers in the same job.
        builder.UseSetting("ConnectionStrings:Sample", string.Empty);

        builder.ConfigureServices(services =>
            services.AddHttpClient(SessionAuthenticationHandler.SchemeName)
                .ConfigurePrimaryHttpMessageHandler(() => new LoopbackHandler(() => Server.CreateHandler())));
    }

    /// <summary>A client that keeps cookies and follows redirects, as a browser does.</summary>
    /// <returns>A client addressing the host over https.</returns>
    internal HttpClient CreateBrowser() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin + "/"),
            HandleCookies = true,
            AllowAutoRedirect = true,
        });

    /// <summary>A client that keeps cookies and reports each redirect instead of taking it.</summary>
    /// <returns>A client addressing the host over https.</returns>
    internal HttpClient CreateFollowingNothing() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin + "/"),
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
}

/// <summary>
/// Forwards an outbound request into a server that does not exist until the host is running.
/// </summary>
internal sealed class LoopbackHandler(Func<HttpMessageHandler> target) : HttpMessageHandler
{
    private HttpMessageInvoker? _invoker;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _invoker ??= new HttpMessageInvoker(target());

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
