using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The publish-budget launch turns telemetry on in the app it launches.
/// </summary>
/// <remarks>
/// <para>
/// <c>scripts/ci/check-publish-budgets.ps1</c> is the only place the OpenTelemetry
/// registration runs inside a real NativeAOT image, so it is the only thing that
/// would catch an AOT-hostile change to it. What makes that gate fragile is that
/// it fails <em>open</em>: an environment variable naming a configuration section
/// the app does not bind matches nothing, <c>TelemetryEnabled</c> stays false,
/// the registration returns early, and the launch still answers /healthz and
/// still passes. Nothing is red. The check has stopped checking.
/// </para>
/// <para>
/// That is what happened on 2026-08-31, when the target moved from apps/web —
/// which bound a "NativeWeb" section — to apps/sample, which binds "Sample".
/// </para>
/// </remarks>
public sealed class TelemetryGateTests
{
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NativeWeb.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException($"Could not locate NativeWeb.slnx above {AppContext.BaseDirectory}.");
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepoRoot(), .. segments]));

    // The section apps/sample hands to NativeWebOptions.FromConfiguration, read
    // from the source rather than restated here: the point of the fact is that
    // the two agree, and a copy of the value in this file would agree with
    // itself while the app moved on.
    private static string SampleConfigSection()
    {
        string program = Read("apps", "sample", "Program.cs");

        const string marker = "NativeWebOptions.FromConfiguration(builder.Configuration, \"";
        int start = program.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "apps/sample no longer binds NativeWebOptions from a named section.");

        start += marker.Length;
        return program[start..program.IndexOf('"', start)];
    }

    [Fact]
    public void TheBudgetLaunch_EnablesTelemetryInTheSectionTheSampleBinds()
    {
        string section = SampleConfigSection();
        string workflow = Read(".github", "workflows", "ci.yml");

        Assert.Contains($"-ConfigPrefix {section}", workflow, StringComparison.Ordinal);

        Assert.Contains(
            "$startInfo.EnvironmentVariables[\"${ConfigPrefix}__Telemetry__Enabled\"] = 'true'",
            Read("scripts", "ci", "check-publish-budgets.ps1"),
            StringComparison.Ordinal);
    }

    // AddNativeWebWeb is what registers OpenTelemetry, and it is the reason no
    // change to apps/sample was needed to restore the gate. If the sample stops
    // calling it, the launch above turns on a switch nothing reads.
    [Fact]
    public void TheSample_RegistersTheFrameworkPipelineThatCarriesTelemetry()
    {
        Assert.Contains("AddNativeWebWeb(options", Read("apps", "sample", "Program.cs"), StringComparison.Ordinal);

        // The link is asserted inside AddNativeWebWeb's own body, sliced from its
        // declaration to the next member at the same indentation. A positional
        // "appears somewhere after" test stayed green when the call was moved into
        // any other method between the two, which is the unhooking it exists to
        // catch.
        string pipeline = Read("libs", "nativeweb.web", "NativeWebPipeline.cs");

        const string declaration = "public static WebApplicationBuilder AddNativeWebWeb(";
        int start = pipeline.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, "NativeWebPipeline.cs no longer declares AddNativeWebWeb.");

        int next = pipeline.IndexOf("\n    public static ", start + declaration.Length, StringComparison.Ordinal);
        Assert.True(next > start, "AddNativeWebWeb is the last member in the file; this test is reading the wrong slice.");

        Assert.Contains(
            "AddNativeWebTelemetry(options",
            pipeline[start..next],
            StringComparison.Ordinal);

        int telemetry = pipeline.IndexOf("public static IServiceCollection AddNativeWebTelemetry", StringComparison.Ordinal);
        Assert.True(telemetry >= 0, "NativeWebPipeline.cs no longer declares AddNativeWebTelemetry.");

        Assert.Contains("services.AddOpenTelemetry()", pipeline[telemetry..], StringComparison.Ordinal);
    }
}
