using System.Diagnostics;
using System.Text.Json;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// <c>NativeWebImportTemplates</c> decides whether a consumer receives the
/// framework's templates at all (spec 029 R5), and this asks MSBuild rather than
/// reading the targets file.
/// </summary>
/// <remarks>
/// <para>
/// The switch is the escape hatch a product reaches for when it wants none of the
/// framework's markup, and it is the only part of spec 029 that no build in this
/// repository exercises: both consumers here leave it at its default, so a typo in
/// the condition, or the property renamed on one side of the file, changes nothing
/// any suite runs. The failure is silent in both directions. Delivery quietly off
/// is four unresolved partials at the next full build; delivery quietly stuck on
/// is a product that cannot opt out and gets a clash error it has no way to clear.
/// </para>
/// <para>
/// Asserted against a throwaway project that imports the targets and is asked for
/// its <c>AdditionalFiles</c>, because that item list is the whole contract: the
/// render generator compiles what is in it. A test reading the XML would assert
/// that the file says what it says, which is true of a broken condition too.
/// </para>
/// <para>
/// One MSBuild invocation here is about a second. No restore, no compile, one
/// target.
/// </para>
/// </remarks>
public sealed class FrameworkTemplateDeliveryTests : IDisposable
{
    private const string ProductCopy = @"templates\shared\bot-traps.html";

    private readonly string _workspace = Directory.CreateTempSubdirectory("nw-029-").FullName;

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    [Fact]
    public void TheSwitchDefaultsToOnAndDeliversEveryTemplateTheFrameworkOwns()
    {
        (int exitCode, string output) = Msbuild(Probe());

        Assert.Equal(0, exitCode);
        Assert.Equal(FrameworkTemplates(), Delivered(output));
    }

    [Fact]
    public void TheSwitchTurnedOffDeliversNothing()
    {
        (int exitCode, string output) = Msbuild(Probe(), "NativeWebImportTemplates=false");

        Assert.Equal(0, exitCode);
        Assert.Empty(Delivered(output));
    }

    /// <summary>
    /// Opting out is how a product keeps a whole tree of copies of its own without
    /// declaring an override for each, so the clash error has to be off with it.
    /// </summary>
    [Fact]
    public void TheSwitchTurnedOffLeavesTheProductsOwnCopyCompiling()
    {
        (int exitCode, string output) = Msbuild(Probe(ProductCopy), "NativeWebImportTemplates=false");

        Assert.Equal(0, exitCode);
        Assert.Empty(Delivered(output));
        Assert.Contains(
            Path.Combine(_workspace, ProductCopy),
            AdditionalFiles(output),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The control for the case above: the same copy, the same project, the switch
    /// left at its default.
    /// </summary>
    /// <remarks>
    /// Without this, a targets file that delivers nothing under any setting passes
    /// both tests above, and the second one reads as proof that the switch
    /// suppressed an error nothing raised.
    /// </remarks>
    [Fact]
    public void TheSameCopyIsABuildErrorWithTheSwitchAtItsDefault()
    {
        (int exitCode, string output) = Msbuild(Probe(ProductCopy));

        Assert.Equal(1, exitCode);
        Assert.Contains(@"exist both in probe and in the framework: shared\bot-traps.html", output);
    }

    /// <summary>
    /// A project that imports the targets and globs its own templates the way both
    /// consumers in this repository do, plus an empty <c>Directory.Build.props</c>
    /// and <c>.targets</c>, so nothing above the temporary directory can join the
    /// build.
    /// </summary>
    private string Probe(params string[] productTemplates)
    {
        foreach (string relative in productTemplates)
        {
            string path = Path.Combine(_workspace, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<p>a copy the product carries</p>\n");
        }

        File.WriteAllText(Path.Combine(_workspace, "Directory.Build.props"), "<Project />\n");
        File.WriteAllText(Path.Combine(_workspace, "Directory.Build.targets"), "<Project />\n");

        string targets = Path.Combine(RepoRoot(), "libs", "nativeweb.web", "build", "NativeWeb.Web.targets");
        string project = Path.Combine(_workspace, "probe.csproj");
        File.WriteAllText(project, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <AdditionalFiles Include="templates\**\*.html" />
              </ItemGroup>
              <Import Project="{targets}" />
            </Project>
            """);

        return project;
    }

    private static (int ExitCode, string Output) Msbuild(string project, params string[] properties)
    {
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add("-t:NativeWebAddFrameworkTemplates");
        start.ArgumentList.Add("-getItem:AdditionalFiles");
        foreach (string property in properties)
        {
            start.ArgumentList.Add($"-p:{property}");
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("dotnet msbuild did not start.");

        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output);
    }

    /// <summary>
    /// Every path in the <c>AdditionalFiles</c> JSON that <c>-getItem</c> printed.
    /// FullPath and not Identity: a framework template arrives absolute and the
    /// product's own arrives relative to the project, and the two sets are only
    /// comparable in one of those forms.
    /// </summary>
    private static string[] AdditionalFiles(string output)
    {
        int start = output.IndexOf('{');

        Assert.True(
            start >= 0,
            $"dotnet msbuild -getItem:AdditionalFiles printed no JSON. It said:{Environment.NewLine}{output}");

        return [.. JsonDocument.Parse(output[start..])
            .RootElement
            .GetProperty("Items")
            .GetProperty("AdditionalFiles")
            .EnumerateArray()
            .Select(item => item.GetProperty("FullPath").GetString()!)];
    }

    /// <summary>
    /// The delivered ones alone, named the way the framework tree holds them.
    /// </summary>
    private static string[] Delivered(string output) =>
        [.. AdditionalFiles(output)
            .Where(f => f.StartsWith(FrameworkTemplateDirectory(), StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(FrameworkTemplateDirectory(), f))
            .Order(StringComparer.Ordinal)];

    private static string[] FrameworkTemplates()
    {
        string[] found = [.. Directory
            .EnumerateFiles(FrameworkTemplateDirectory(), "*.html", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(FrameworkTemplateDirectory(), f))
            .Order(StringComparer.Ordinal)];

        Assert.NotEmpty(found);

        return found;
    }

    private static string FrameworkTemplateDirectory() =>
        Path.Combine(RepoRoot(), "libs", "nativeweb.web", "templates");

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
