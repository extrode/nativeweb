using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Npgsql;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

/// <summary>
/// Two registrations whose defects are invisible from a request: what owns the
/// data source's lifetime, and what the antiforgery cookie is called when the
/// secure policy relaxes for Development.
/// </summary>
public sealed class PipelineRegistrationTests
{
    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Demo";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static NativeWebOptions Options(string? connectionString = null) => new()
    {
        ApplicationName = "Demo",
        ConfigurationSection = "Demo",
        TelemetryServiceName = "Demo",
        DataProtectionKeyDirectory = Path.Combine(AppContext.BaseDirectory, "dp-keys"),
        ConnectionString = connectionString,
    };

    [Fact]
    public void TheContainerOwnsTheDataSourceAndDrainsItAtShutdown()
    {
        // A singleton registered as an already-built instance is not disposed by
        // the container -- it disposes what it creates and nothing else. The pool
        // would then outlive the host: connections closed by the socket rather
        // than by Npgsql, and a shutdown that does not drain. A factory
        // registration is the whole fix, and nothing about a served request can
        // tell the two apart, which is why the fact is asserted here.
        var services = new ServiceCollection();
        services.AddNativeWebDataSource(Options("Host=localhost;Port=1;Database=nowhere;Timeout=1"));

        // The registration shape, read directly: an instance the container did
        // not create is an instance it will not dispose.
        ServiceDescriptor descriptor = Assert.Single(
            services, d => d.ServiceType == typeof(NpgsqlDataSource));
        Assert.Null(descriptor.ImplementationInstance);
        Assert.NotNull(descriptor.ImplementationFactory);

        // And the consequence of that shape. CreateConnection does not check --
        // it hands back an unopened NpgsqlConnection either way -- so the probe
        // is OpenConnection, whose disposed check runs before it reaches a
        // socket. Host=localhost:1 is unreachable on purpose: a live pool would
        // fail this line with a connection error, and only a disposed one gives
        // ObjectDisposedException.
        NpgsqlDataSource dataSource;
        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            dataSource = provider.GetRequiredService<NpgsqlDataSource>();
        }

        Assert.Throws<ObjectDisposedException>(() => dataSource.OpenConnection());
    }

    [Fact]
    public void NoDataSourceIsRegisteredWithoutAConnectionString()
    {
        // The control: absence is a running state, not a failure.
        var services = new ServiceCollection();
        services.AddNativeWebDataSource(Options());

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<NpgsqlDataSource>());
    }

    [Theory]
    [InlineData("Production", "__Host-Demo-antiforgery", CookieSecurePolicy.Always)]
    [InlineData("Development", "Demo-antiforgery", CookieSecurePolicy.SameAsRequest)]
    public void TheDevelopmentCookieIsTheProductionCookieWithoutItsPrefix(
        string environment, string expectedName, CookieSecurePolicy expectedPolicy)
    {
        // One name, two forms. The prefix and Secure travel together -- a browser
        // rejects a __Host- cookie that is not Secure -- so Development drops
        // both. Composing the relaxed name separately would let a rename of
        // AntiforgeryCookieName move production's cookie and leave Development's
        // where it was, and no test of a form post would notice.
        var services = new ServiceCollection();
        services.AddNativeWebAntiforgery(Options(), new Environment(environment));

        using ServiceProvider provider = services.BuildServiceProvider();
        AntiforgeryOptions antiforgery = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        Assert.Equal(expectedName, antiforgery.Cookie.Name);
        Assert.Equal(expectedPolicy, antiforgery.Cookie.SecurePolicy);
        Assert.Equal("RequestVerificationToken", antiforgery.HeaderName);
    }
}
