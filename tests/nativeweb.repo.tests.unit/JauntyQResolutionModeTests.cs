using System.Diagnostics;
using System.Text.Json;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// Which JauntyQ this repository builds against, asked of MSBuild rather than read off
/// <c>Directory.Build.props</c>.
/// </summary>
/// <remarks>
/// <para>
/// JauntyQ split on 2026-09-05: the generator and runtime sources moved to a public core
/// repository, and the sibling <c>jauntyq/</c> checkout stopped carrying either csproj this
/// repository referenced. Package mode became the default that day
/// (<c>docs/decisions/2026-09-05-012-jauntyq-package-mode-default.md</c>), and the default is
/// the whole point: a build that silently reverted to checkout mode fails at
/// <c>EnsureJauntyQRoot</c> with a message about a missing directory, which reads as a broken
/// machine rather than a broken condition.
/// </para>
/// <para>
/// Three conditions have to agree for a mode to resolve, in two files
/// (<c>Directory.Build.props</c>, <c>apps/sample/nativeweb.sample.csproj</c>) plus the
/// skeleton's pair. Inverting one of them and not the rest produces a project that references
/// JauntyQ twice or not at all, and neither is a compile error a suite here would notice.
/// </para>
/// <para>
/// <c>-getProperty</c> and <c>-getItem</c> evaluate without restoring or compiling, so each
/// case is about a second.
/// </para>
/// </remarks>
public sealed class JauntyQResolutionModeTests
{
    private static readonly string[] ResolutionProperties =
        ["JauntyQUsePackages", "NativeWebUsePackages"];

    private static readonly string SampleProject =
        Path.Combine(RepoRoot(), "apps", "sample", "nativeweb.sample.csproj");

    private static readonly string SkeletonProject =
        Path.Combine(RepoRoot(), "skeleton", "libs", "__product__.data", "__product__.data.csproj");

    [Fact]
    public void PackagesAreTheDefaultWithNoFlagOnTheCommandLine()
    {
        Assert.Equal("true", Property(SampleProject, "JauntyQUsePackages"));
    }

    [Fact]
    public void TheSkeletonResolvesTheSameWayAStampedProductWill()
    {
        Assert.Equal("true", Property(SkeletonProject, "JauntyQUsePackages"));
    }

    /// <summary>
    /// The framework's own resolution is not flipped with JauntyQ's: no NativeWeb.* package has
    /// published, so a stamped product still needs the framework checkout.
    /// </summary>
    [Fact]
    public void TheSkeletonStillDefaultsToANativeWebCheckout()
    {
        Assert.Equal(string.Empty, Property(SkeletonProject, "NativeWebUsePackages"));
    }

    [Fact]
    public void CheckoutModeIsReachedByAskingForItAndOnlyByAskingForIt()
    {
        Assert.Equal("false", Property(SampleProject, "JauntyQUsePackages", "JauntyQUsePackages=false"));
    }

    [Fact]
    public void TheDefaultReferencesJauntyQAsPackagesAndNotAsProjects()
    {
        Assert.NotEmpty(JauntyQ(Items(SampleProject, "PackageReference")));
        Assert.Empty(JauntyQ(Items(SampleProject, "ProjectReference")));
    }

    /// <summary>
    /// The two modes are exclusive. Without this, a condition inverted on one side and left on
    /// the other still passes every case above.
    /// </summary>
    [Fact]
    public void CheckoutModeReferencesJauntyQAsProjectsAndNotAsPackages()
    {
        Assert.NotEmpty(JauntyQ(Items(SampleProject, "ProjectReference", "JauntyQUsePackages=false")));
        Assert.Empty(JauntyQ(Items(SampleProject, "PackageReference", "JauntyQUsePackages=false")));
    }

    /// <summary>
    /// The solution names only projects that exist. It listed JauntyQ's two until the split, and
    /// a member pointing at a missing csproj is MSB3202 at restore, before any condition here is
    /// evaluated.
    /// </summary>
    [Fact]
    public void TheSolutionNamesNoJauntyQProject()
    {
        string solution = File.ReadAllText(Path.Combine(RepoRoot(), "NativeWeb.slnx"));

        Assert.DoesNotContain("jauntyq", solution, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] JauntyQ(string[] identities) =>
        [.. identities.Where(i => i.Contains("JauntyQ", StringComparison.OrdinalIgnoreCase))];

    private static string Property(string project, string name, params string[] properties)
    {
        (int exitCode, string output) = Msbuild(project, $"-getProperty:{name}", properties);

        Assert.Equal(0, exitCode);

        return output.Trim();
    }

    private static string[] Items(string project, string name, params string[] properties)
    {
        (int exitCode, string output) = Msbuild(project, $"-getItem:{name}", properties);

        Assert.Equal(0, exitCode);

        int start = output.IndexOf('{');

        Assert.True(
            start >= 0,
            $"dotnet msbuild -getItem:{name} printed no JSON. It said:{Environment.NewLine}{output}");

        JsonElement items = JsonDocument.Parse(output[start..]).RootElement.GetProperty("Items");

        return items.TryGetProperty(name, out JsonElement list)
            ? [.. list.EnumerateArray().Select(i => i.GetProperty("Identity").GetString()!)]
            : [];
    }

    private static (int ExitCode, string Output) Msbuild(
        string project,
        string query,
        params string[] properties)
    {
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // MSBuild reads a bare environment variable as a property, so an inherited
        // JauntyQUsePackages answers every question below before Directory.Build.props is
        // consulted. Measured 2026-09-05: with JauntyQUsePackages=true exported, deleting the
        // default assignment outright left all seven facts green.
        foreach (string inherited in ResolutionProperties)
        {
            start.Environment.Remove(inherited);
        }

        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add(query);
        foreach (string property in properties)
        {
            start.ArgumentList.Add($"-p:{property}");
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("dotnet msbuild did not start.");

        // stderr is drained on its own task rather than after stdout: draining them in sequence
        // deadlocks whenever the second pipe's buffer fills while the first is still being read.
        Task<string> errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd() + errors.GetAwaiter().GetResult();
        process.WaitForExit();

        return (process.ExitCode, output);
    }

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
}
