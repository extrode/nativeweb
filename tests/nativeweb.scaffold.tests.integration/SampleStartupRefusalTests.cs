using System.Diagnostics;

using NativeWeb.Web;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// A refused configuration reaches the process boundary as a message and an exit code.
/// </summary>
/// <remarks>
/// <para>
/// The unit tier proves each branch of <c>TryFromConfiguration</c> returns false and
/// names its key. That is a different fact from the one that matters to an operator:
/// that the refusal reaches the process boundary as a message on stderr and a
/// non-zero exit, rather than as an unhandled exception.
/// </para>
/// <para>
/// The distinction is not theoretical. <c>lessons/2026-07-26</c> records the failure
/// mode: an exception out of Main under NativeAOT fail-fasts with 0xC0000409, which
/// on Windows opens a modal error box and blocks the process on it. A supervisor
/// then sees a hung service instead of a configuration error, and nothing in the
/// unit tier can tell the two apart. The timeout in <c>RunToExit</c> is that
/// assertion.
/// </para>
/// <para>
/// The lever is a malformed value rather than the key directory, which is the branch
/// that prompted this test. An unusable home cannot be produced from the environment
/// on Windows: <c>GetFolderPath(SpecialFolder.UserProfile)</c> reads the process
/// token there, not <c>USERPROFILE</c>, so that branch is unreachable in a child
/// process and stays a unit-tier fact. Every refusal leaves through the same
/// <c>FailStartup</c>, which is what this proves.
/// </para>
/// </remarks>
public sealed class SampleStartupRefusalTests
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

    private static Process Start(string variable, string value, string? workingDirectory = null)
    {
        // The configuration this test itself was built in, read off its own output
        // path: hardcoding Debug passes locally and finds nothing in a Release run.
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string assembly = Path.Combine(
            RepoRoot(), "apps", "sample", "bin", configuration, "net10.0", "nativeweb.sample.dll");

        Assert.True(File.Exists(assembly), $"{assembly} is not built; this test cannot run the sample.");

        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,

            // wwwroot is not copied beside the assembly, so the content root has to
            // be the project directory or the host dies on the static error shell
            // instead of on the value under test. The one test that IS about the
            // shell passes a directory without one.
            WorkingDirectory = workingDirectory ?? Path.Combine(RepoRoot(), "apps", "sample"),
        };

        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(assembly);

        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        start.Environment[variable] = value;

        return Process.Start(start)!;
    }

    private static (int ExitCode, string StandardError) RunToExit(
        string variable, string value, string? workingDirectory = null)
    {
        using Process process = Start(variable, value, workingDirectory);
        string standardError = process.StandardError.ReadToEnd();

        Assert.True(
            process.WaitForExit(30_000),
            "The sample did not exit within 30s of a refused configuration. That is the modal-block "
                + "failure mode in lessons/2026-07-26: an unhandled exception out of Main rather than a "
                + "reported error and a clean exit.");

        return (process.ExitCode, standardError);
    }

    [Theory]
    [InlineData("Sample__BotTraps__MinAgeSeconds", "-1", "Sample:BotTraps:MinAgeSeconds")]
    [InlineData("Sample__Telemetry__Enabled", "maybe", "Sample:Telemetry:Enabled")]
    public void ARefusedConfiguration_EndsTheProcessWithTheConfigurationExitCode(
        string variable, string value, string expectedKey)
    {
        (int exitCode, string standardError) = RunToExit(variable, value);

        Assert.Equal(StartupConfigGuards.ConfigurationErrorExitCode, exitCode);
        Assert.Contains(expectedKey, standardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// A missing static error shell leaves by the same door as a refused value.
    /// </summary>
    /// <remarks>
    /// The value passed is one <see cref="AValueThatBinds_LeavesTheHostRunning"/>
    /// proves keeps the host up, so the content root without a <c>wwwroot</c> is
    /// the only thing wrong here. Before the fix this exited 0xE0434352, an
    /// unhandled exception out of Main: the shell is loaded while the pipeline is
    /// being built, and nothing above it catches.
    /// </remarks>
    [Fact]
    public void AMissingStaticErrorShell_EndsTheProcessWithTheConfigurationExitCode()
    {
        (int exitCode, string standardError) =
            RunToExit("Sample__BotTraps__MinAgeSeconds", "2", RepoRoot());

        Assert.Equal(StartupConfigGuards.ConfigurationErrorExitCode, exitCode);
        Assert.Contains("pages/error.html", standardError, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueThatBinds_LeavesTheHostRunning()
    {
        using Process process = Start("Sample__BotTraps__MinAgeSeconds", "2");

        try
        {
            Assert.False(
                process.WaitForExit(5_000),
                "The sample exited on a value that binds, so the tests above prove nothing about "
                    + "which values are refused.");
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
    }
}
