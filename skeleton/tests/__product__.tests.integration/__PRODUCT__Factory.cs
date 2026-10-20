using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace __PRODUCT__.Tests.Integration;

// Boots the real Program.cs composition root. Locally, whatever
// __PRODUCT__:Db:ConnectionString this machine already has configured for
// `dotnet run` (user-secrets) is picked up the normal way; CI has no
// user-secrets store, so it sets __PRODUCT____Db__ConnectionString as a real
// environment variable pointing at its Postgres service container instead.
// That value is layered on top here so it always wins when present.
public sealed class __PRODUCT__Factory : WebApplicationFactory<Program>
{
    // CreateSlimBuilder's Configuration doesn't reliably see
    // WebApplicationFactory's in-memory overrides; env vars are the one
    // mechanism proven to reach it.
    static __PRODUCT__Factory()
    {
        // Bot-trap MinAge would trip on machine-fast submits; the trap fields
        // themselves are still posted for real by the forms a product adds.
        Environment.SetEnvironmentVariable("__PRODUCT____BotTraps__MinAgeSeconds", "0");

        // The whole in-proc suite shares one "unknown" IP partition, so the
        // production rate-limit windows would throttle the tests, not a
        // client. Environment variables because they are the one source
        // minimal hosting reliably lets a factory override (they beat host
        // config).
        Environment.SetEnvironmentVariable("__PRODUCT____RateLimit__General__PermitLimit", "100000");
        Environment.SetEnvironmentVariable("__PRODUCT____RateLimit__CspReport__PermitLimit", "100000");
    }

    // https rather than the default http://localhost base: a product that adds
    // sign-in gets a __Host- prefixed session cookie, which is Secure-always.
    // Clients that let the handler manage cookies silently drop such a cookie
    // over http -- CookieContainer never sends a Secure cookie to an http
    // origin, unlike a real browser, which treats localhost as a secure
    // context. An https base makes every client see cookies the way a browser
    // does; TestServer accepts it without any TLS handshake.
    public __PRODUCT__Factory()
    {
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            string? connectionString = Environment.GetEnvironmentVariable("__PRODUCT____Db__ConnectionString");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["__PRODUCT__:Db:ConnectionString"] = connectionString,
                });
            }
        });
    }
}

[CollectionDefinition(Name)]
public sealed class __PRODUCT__Collection : ICollectionFixture<__PRODUCT__Factory>
{
    public const string Name = "__PRODUCT__";
}
