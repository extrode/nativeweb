using System.Diagnostics;
using System.Text.RegularExpressions;

using Xunit;

namespace NativeWeb.SignIn.Tests.Aot;

/// <summary>
/// Spec 016 R25: a host referencing the library publishes NativeAOT with no trim or AOT
/// analyser warning.
/// </summary>
/// <remarks>
/// <para>
/// The publish is the assertion. <c>TreatWarningsAsErrors</c> is off in <c>probe-host</c>
/// alone, so an analyser finding arrives as a warning line rather than stopping the link on
/// the first one — a gate that fails with one warning cannot say whether there were nine.
/// The suppression is why this tier reads the log instead of trusting the exit code.
/// </para>
/// <para>
/// One publish, minutes long. It is a separate tier rather than a fact in the unit suite for
/// that reason: <c>dotnet test tests/nativeweb.signin.tests.unit</c> stays a sub-second loop.
/// </para>
/// </remarks>
public sealed class AotPublishGate
{
    /// <summary>
    /// Ceiling on the one publish. Thirty minutes, matching the e2e tiers: measured at 21 s
    /// on an idle machine, and the number exists for a hung link rather than a slow one.
    /// </summary>
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Warnings the analysers raise about trimming, single-file and AOT. IL2xxx is the trim
    /// analyser, IL3xxx AOT and single-file; the two families are what R25 is about, and
    /// matching the prefix rather than a list means a code introduced by a future SDK is
    /// caught rather than skipped.
    /// </summary>
    private static readonly Regex AnalyserWarning = new(
        @"warning\s+IL[23]\d{3}",
        RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Publish_OfAHostReferencingTheLibrary_RaisesNoTrimOrAotWarning()
    {
        (int exitCode, string log) = await PublishAsync();

        string[] warnings = log
            .Split('\n')
            .Where(line => AnalyserWarning.IsMatch(line))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(warnings);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Publish_OfAHostReferencingTheLibrary_ProducesANativeBinary()
    {
        await PublishAsync();

        string exe = Path.Combine(
            ProbeHostDirectory(),
            "bin", "Release", "net10.0", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            "publish",
            OperatingSystem.IsWindows() ? "probe-host.exe" : "probe-host");

        Assert.True(File.Exists(exe), $"Expected a published binary at {exe}.");
    }

    // The publish runs once however many facts want it: two `dotnet publish` runs against one
    // project race for the same intermediates, and ILC fails on a locked file several layers
    // from anything that mentions parallelism. Lazy<Task> rather than a lock so the second
    // caller awaits the first run's result and observes its failure.
    private static readonly Lazy<Task<(int ExitCode, string Log)>> Published = new(RunPublishAsync);

    private static Task<(int ExitCode, string Log)> PublishAsync() => Published.Value;

    private static async Task<(int ExitCode, string Log)> RunPublishAsync()
    {
        string csproj = Path.Combine(ProbeHostDirectory(), "probe-host.csproj");

        var startInfo = new ProcessStartInfo("dotnet", $"publish \"{csproj}\" -c Release")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet publish.");

        // Redirecting a stream obliges someone to drain it: a full pipe buffer blocks the
        // publish forever on its next write, and the tier hangs rather than fails.
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(PublishTimeout);

        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The AOT publish exceeded {PublishTimeout}.");
        }

        string log = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);

        return (process.ExitCode, log);
    }

    private static string ProbeHostDirectory() =>
        Path.Combine(RepoRoot(), "tests", "nativeweb.signin.tests.aot", "probe-host");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NativeWeb.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"No NativeWeb.slnx above {AppContext.BaseDirectory}.");
    }
}
