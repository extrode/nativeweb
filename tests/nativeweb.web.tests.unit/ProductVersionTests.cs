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
/// The two are the same in a published binary. A stamped product's integration
/// tier reported <c>18.7.0</c> — the VSTest test host's version — and the only
/// thing that noticed was a regex expecting <c>Version+shortsha</c>.
/// </para>
/// <para>
/// Under xunit v3 the test assembly is itself the executable, so
/// <see cref="Assembly.GetEntryAssembly"/> answers with this very assembly and the
/// fallback can no longer be told apart by naming it. Every assembly built from
/// this repository shares one informational version, so naming a sibling would not
/// separate them either. The second fact below reaches outside the repository for
/// an assembly whose version cannot coincide.
/// </para>
/// <para>
/// The fix was a parameter rather than
/// <c>Assembly.Load(new AssemblyName(environment.ApplicationName))</c>, which
/// resolves under NativeAOT (the assembly is already in the image) but answers a
/// rename with a silent fallback instead of a compile error. These two facts are
/// the pair: omitting the assembly falls back to whatever the entry assembly is,
/// and naming one decides the answer.
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
            UploadDirectory = Path.Combine(AppContext.BaseDirectory, "uploads"),
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

        // The whole reason the parameter exists. Naming an assembly has to change
        // the answer, or the parameter is decorative and the fact above is just
        // this assembly agreeing with itself. Corelib because it is the one
        // assembly in reach whose informational version cannot collide with the
        // repository's own.
        Assert.NotEqual(reported, await VersionAsync(typeof(object).Assembly));
    }
}
