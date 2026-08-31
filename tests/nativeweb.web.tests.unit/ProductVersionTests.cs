using System.Reflection;

using Microsoft.AspNetCore.TestHost;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

/// <summary>
/// <c>/version</c> reports the assembly the product names, not whichever assembly
/// happens to have started the process.
/// </summary>
/// <remarks>
/// <para>
/// The two are the same in a published binary and different under every test
/// runner, where the entry assembly is testhost. A stamped product's integration
/// tier reported <c>18.7.0</c> — testhost's version — and the only thing that
/// noticed was a regex expecting <c>Version+shortsha</c>.
/// </para>
/// <para>
/// The fix was a parameter rather than
/// <c>Assembly.Load(new AssemblyName(environment.ApplicationName))</c>, which
/// resolves under NativeAOT (the assembly is already in the image) but answers a
/// rename with a silent fallback instead of a compile error. These two facts are
/// the pair: naming the assembly decides the answer, and omitting it falls back
/// to the entry assembly, which under this runner is testhost.
/// </para>
/// </remarks>
public sealed class ProductVersionTests
{
    private static async Task<string> VersionAsync(Assembly? productAssembly)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var options = new NativeWebOptions
        {
            ApplicationName = "Demo",
            ConfigurationSection = "Demo",
            TelemetryServiceName = "Demo",
            DataProtectionKeyDirectory = Path.Combine(AppContext.BaseDirectory, "dp-keys"),
        };

        builder.AddNativeWebWeb(options, productAssembly);

        WebApplication app = builder.Build();
        app.MapNativeWebEndpoints(options, productAssembly);

        await app.StartAsync();

        try
        {
            HttpClient client = app.GetTestClient();
            client.BaseAddress = new Uri("http://localhost/");

            return await client.GetStringAsync(new Uri("/version", UriKind.Relative));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task TheNamedAssemblyIsWhatIsReported()
    {
        Assembly named = typeof(ProductVersionTests).Assembly;
        string expected = named
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        Assert.Equal(expected, await VersionAsync(named));
        Assert.NotEqual("unknown", expected);
    }

    [Fact]
    public async Task OmittingItFallsBackToTheEntryAssembly()
    {
        string reported = await VersionAsync(null);

        string? entry = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        Assert.Equal(entry ?? "unknown", reported);

        // The whole reason the parameter exists: under a test runner the fallback
        // is not this assembly. If these ever match, the fallback has stopped
        // being distinguishable and this pair of facts asserts nothing.
        Assert.NotEqual(
            typeof(ProductVersionTests).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion,
            reported);
    }
}
