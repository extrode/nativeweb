using System.Text.Json;
using System.Xml.Linq;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The repository runs its tests on xUnit v3 over Microsoft.Testing.Platform, and
/// every piece of that wiring is a file nothing else reads.
/// </summary>
/// <remarks>
/// <para>
/// Four separate facts, each of which fails silently or misleadingly on its own.
/// A project left on the VSTest packages does not fail this repository's build; it
/// fails at run time with a message about a target no longer being supported. A
/// project that references xunit.v3 without <c>OutputType=Exe</c> fails the build,
/// so that one is loud — but the inverse, a library that acquires xunit.v3 and is
/// forced to become an executable, is exactly the mistake
/// <c>libs/nativeweb.signin.testing</c> exists to avoid and would be found by
/// nobody until a consumer restored the package.
/// </para>
/// <para>
/// The two that would be quietest are the last. <c>global.json</c> is the whole
/// opt-in: remove the <c>test</c> section and every `dotnet test` in the
/// repository fails at once, which is at least obvious, but remove it from
/// <c>skeleton/global.json</c> alone and only a stamped product notices. And
/// <c>--nologo</c> is not an error under the new CLI — it is forwarded to the test
/// executable, which rejects it, runs nothing and exits 5. A CI step that runs no
/// tests is the one failure mode this repository has already been bitten by
/// (`tools/stamp-check.ts` exists because of it).
/// </para>
/// </remarks>
public sealed class TestRunnerWiringTests
{
    /// <summary>The packages the v3 move dropped. None may come back.</summary>
    private static readonly string[] Departed =
    [
        "xunit",
        "xunit.runner.visualstudio",
        "Microsoft.NET.Test.Sdk",
    ];

    [Fact]
    public void NoProjectReferencesThePackagesTheV3MoveDropped()
    {
        string[] offenders = [.. Projects()
            .SelectMany(p => PackageReferences(p).Select(r => (Project: Relative(p), Package: r)))
            .Where(x => Departed.Contains(x.Package, StringComparer.OrdinalIgnoreCase))
            .Select(x => $"{x.Project} -> {x.Package}")
            .Order(StringComparer.Ordinal)];

        Assert.True(
            offenders.Length == 0,
            "These are the VSTest packages, and the .NET 10 SDK refuses to drive xunit.v3 through "
                + "the VSTest target at all, so a project carrying one does not fail the build - it "
                + $"fails when someone runs it: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void EveryProjectThatRunsTestsIsAnExecutableAndEveryOtherOneIsNot()
    {
        string[] runners = [.. Projects()
            .Where(p => PackageReferences(p).Contains("xunit.v3", StringComparer.OrdinalIgnoreCase))];

        Assert.True(
            runners.Length >= 20,
            $"Found {runners.Length} projects referencing xunit.v3 and expected the whole test tree. "
                + "The reference has been renamed or moved into a shared props file, and everything "
                + "below is now asserting about a handful of projects rather than all of them.");

        string[] notExecutable = [.. runners
            .Where(p => Property(p, "OutputType") != "Exe")
            .Select(Relative)
            .Order(StringComparer.Ordinal)];

        Assert.True(
            notExecutable.Length == 0,
            "xunit.v3 carries the in-process runner and refuses to build a project that is not an "
                + $"executable: {string.Join(", ", notExecutable)}");

        // The other direction. xunit.v3 in a packable library is the mistake
        // libs/nativeweb.signin.testing is shaped to avoid, and it does not fail
        // here - it fails in a consumer's restore, or worse, ships.
        string[] packableRunners = [.. runners
            .Where(p => Property(p, "IsPackable") == "true")
            .Select(Relative)
            .Order(StringComparer.Ordinal)];

        Assert.True(
            packableRunners.Length == 0,
            "These are packable and reference xunit.v3, which makes them executables and puts a "
                + "test runner in a consumer's package. The runner-less halves are "
                + $"xunit.v3.extensibility.core and xunit.v3.assert: {string.Join(", ", packableRunners)}");
    }

    [Theory]
    [InlineData("global.json")]
    [InlineData("skeleton/global.json")]
    public void TheRunnerOptInIsPresent(string path)
    {
        string full = Path.Combine(RepoRoot(), path.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(full), $"{path} does not exist.");

        using var document = JsonDocument.Parse(File.ReadAllText(full));

        bool found = document.RootElement.TryGetProperty("test", out JsonElement test)
            && test.TryGetProperty("runner", out JsonElement runner)
            && runner.GetString() == "Microsoft.Testing.Platform";

        Assert.True(
            found,
            $"{path} is missing \"test\": {{ \"runner\": \"Microsoft.Testing.Platform\" }}. Without "
                + "it `dotnet test` uses the VSTest command, which on the .NET 10 SDK refuses every "
                + "xunit.v3 project in the tree.");
    }

    /// <summary>
    /// Every `dotnet test` command in a workflow, a script or the docs names its
    /// project with <c>--project</c> and passes no <c>--nologo</c>.
    /// </summary>
    /// <remarks>
    /// <c>--nologo</c> is the dangerous half. The new CLI does not reject it: it
    /// forwards it to the test executable, which does, so the step runs no tests
    /// and exits 5. A bare path is merely fatal - "Specifying a project for
    /// 'dotnet test' should be via '--project'".
    /// </remarks>
    [Fact]
    public void EveryDotnetTestCommandUsesTheNewCliShape()
    {
        string root = RepoRoot();

        string[] sources =
        [
            ".github/workflows/ci.yml",
            ".github/workflows/publish.yml",
            "skeleton/.github/workflows/ci.yml",
            "README.md",
            "skeleton/README.md",
            "docs/00-quick-start/getting-started.md",
            "docs/00-quick-start/tutorial-first-feature.md",
            "tools/stamp-check.ts",
        ];

        List<string> commands = [];
        List<string> wrong = [];

        foreach (string source in sources)
        {
            string full = Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(
                File.Exists(full),
                $"{source} does not exist, so this test reads one file fewer than it claims to.");

            foreach (string line in File.ReadLines(full))
            {
                // The literal command, not a prose mention: a backtick or a comma
                // straight after "dotnet test" is someone writing about it.
                int at = line.IndexOf("dotnet test ", StringComparison.Ordinal);

                if (at < 0) continue;

                string command = line[at..];
                commands.Add($"{source}: {command.Trim()}");

                if (!command.Contains("--project", StringComparison.Ordinal)
                    || command.Contains("--nologo", StringComparison.Ordinal))
                {
                    wrong.Add($"{source}: {command.Trim()}");
                }
            }
        }

        Assert.True(
            commands.Count >= 20,
            $"Found {commands.Count} `dotnet test` commands across {sources.Length} files and "
                + "expected the whole suite list. The runs have moved somewhere this test does not "
                + "read, and it is now guarding almost nothing.");

        Assert.True(
            wrong.Count == 0,
            "Each of these either names its project without --project, which is fatal, or passes "
                + "--nologo, which is worse: the new CLI forwards it to the test executable, which "
                + $"rejects it and runs no tests at all. {string.Join(" | ", wrong)}");
    }

    /// <summary>
    /// Every package a project references is pinned in the <c>Directory.Packages.props</c>
    /// that governs it, and the skeleton has its own.
    /// </summary>
    /// <remarks>
    /// Central package management turns a missing pin into NU1010 at restore, so this
    /// looks redundant - and it is, for everything under <c>tests/</c>, which the suites
    /// build. It is not redundant for <c>skeleton/</c>: nothing in this repository
    /// restores those projects, because they are a template stamped into a new product
    /// first. The skeleton going to xunit.v3 while its pins stayed on 2.9.3 built and
    /// tested green here and failed on the stamped product, which is a failure only
    /// `tools/stamp-check.ts` runs late enough to catch.
    /// </remarks>
    [Fact]
    public void EveryPackageAProjectReferencesIsPinnedWhereThatProjectLooks()
    {
        string[] unpinned = [.. Projects()
            .SelectMany(p => PackageReferences(p).Select(r => (Project: p, Package: r)))
            .Where(x => !PinsFor(x.Project).Contains(x.Package, StringComparer.OrdinalIgnoreCase))
            .Select(x => $"{Relative(x.Project)} -> {x.Package}")
            .Order(StringComparer.Ordinal)];

        Assert.True(
            unpinned.Length == 0,
            "Central package management requires a PackageVersion for every PackageReference, "
                + "and the skeleton's props file is a separate one nothing in this repository "
                + $"restores: {string.Join(", ", unpinned)}");
    }

    /// <summary>
    /// The pin names from the nearest <c>Directory.Packages.props</c> at or above a
    /// project, which is the one MSBuild imports: it stops at the first it finds.
    /// </summary>
    private static string[] PinsFor(string project)
    {
        DirectoryInfo? dir = new(Path.GetDirectoryName(project)!);

        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Directory.Packages.props");

            if (File.Exists(candidate))
            {
                return [.. XDocument.Load(candidate)
                    .Descendants()
                    .Where(e => e.Name.LocalName == "PackageVersion")
                    .Select(e => (string?)e.Attribute("Include"))
                    .Where(i => i is not null)
                    .Select(i => i!)];
            }

            dir = dir.Parent;
        }

        return [];
    }

    /// <summary>`tools/stamp-check.ts` is TypeScript, so its commands are quoted arguments.</summary>
    private static string[] PackageReferences(string project) =>
        [.. XDocument.Load(project)
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(i => i is not null)
            .Select(i => i!)];

    private static string? Property(string project, string name) =>
        XDocument.Load(project)
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == name)
            ?.Value
            .Trim();

    /// <summary>
    /// Every csproj in the tree, build residue and worktrees aside. A worktree is
    /// a full checkout, so without the exclusion every project is found once more
    /// per worktree and reported under a path nobody edits.
    /// </summary>
    private static string[] Projects()
    {
        string root = RepoRoot();

        string[] found = [.. Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !Excluded(Path.GetRelativePath(root, p).Replace('\\', '/')))
            .Order(StringComparer.Ordinal)];

        Assert.NotEmpty(found);

        return found;
    }

    private static bool Excluded(string relative) =>
        relative.StartsWith("tmp/", StringComparison.Ordinal)
        || relative.StartsWith(".worktrees/", StringComparison.Ordinal)
        || relative.StartsWith(".claude/", StringComparison.Ordinal)
        || relative.Contains("/bin/", StringComparison.Ordinal)
        || relative.Contains("/obj/", StringComparison.Ordinal);

    private static string Relative(string project) =>
        Path.GetRelativePath(RepoRoot(), project).Replace('\\', '/');

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
