using System.Text.RegularExpressions;
using System.Xml.Linq;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// `nuget.config` and the workflow that authenticates against it have to agree,
/// and the mapping has to stay total.
/// </summary>
/// <remarks>
/// <para>
/// The gap these guard was invisible for months because a developer machine
/// cannot see it: package-mode restore resolved JauntyQ.* out of a
/// ~/.nuget/packages directory that a local `dotnet pack` had already filled, so
/// the cache stood in for a feed that was never configured. Only a clean runner
/// noticed, as NU1101.
/// </para>
/// <para>
/// Two ways it can rot again, both silent locally and both fatal on a runner: the
/// source gets renamed in one file and not the other, so the workflow
/// authenticates a source that does not exist; or the "*" catch-all is dropped
/// from the mapping, which is not a partial failure but a total one, because
/// package source mapping resolves nothing that matches no pattern.
/// </para>
/// </remarks>
public sealed partial class PackageFeedConfigTests
{
    private const string JauntyQSource = "jauntyq-github";

    [Fact]
    public void NuGetConfig_MapsJauntyQPackagesToTheGitHubPackagesSource()
    {
        var config = XDocument.Load(Path.Combine(RepoRoot(), "nuget.config"));

        XElement? source = config.Descendants("packageSources")
            .Elements("add")
            .FirstOrDefault(e => (string?)e.Attribute("key") == JauntyQSource);

        Assert.True(source is not null, $"nuget.config declares no '{JauntyQSource}' package source.");

        // The owner segment is the whole point of this assertion, not incidental
        // detail. A GITHUB_TOKEN is scoped to its own repository, so it can only
        // read packages owned by that repository's org: when both repos moved
        // from extrode-legacy-org to extrode on 2026-08-26 and the packages did not follow,
        // this URL kept naming extrode-legacy-org and every CI restore died NU1301 "403
        // (Forbidden)" for three days. Pointing it at an org whose packages this
        // repo cannot read fails only on a runner, never here.
        Assert.Equal("https://nuget.pkg.github.com/extrode/index.json", (string?)source!.Attribute("value"));

        XElement? mapping = config.Descendants("packageSourceMapping")
            .Elements("packageSource")
            .FirstOrDefault(e => (string?)e.Attribute("key") == JauntyQSource);

        Assert.True(mapping is not null, $"nuget.config maps nothing to '{JauntyQSource}'.");
        Assert.Contains(
            mapping!.Elements("package"),
            p => (string?)p.Attribute("pattern") == "JauntyQ.*");
    }

    /// <summary>
    /// Only the GitHub source may claim a JauntyQ pattern.
    /// </summary>
    /// <remarks>
    /// This is the dependency-confusion shape, and it is the one mis-edit here
    /// that NuGet accepts without complaint. Mapping resolves by most specific
    /// pattern, so an identical "JauntyQ.*" added under nuget.org puts both
    /// sources in play for those ids and a public package could answer for a
    /// private one. Restore stays green and nothing in the log says so.
    ///
    /// The neighbouring invariant — that the mapping keeps a "*" catch-all —
    /// deliberately has no test: drop it and NuGet refuses the file outright
    /// ("Package source 'nuget.org' must have at least one package pattern"),
    /// which is a louder failure than any assertion here could add.
    /// </remarks>
    [Fact]
    public void NuGetConfig_LetsNoOtherSourceClaimAJauntyQPattern()
    {
        var config = XDocument.Load(Path.Combine(RepoRoot(), "nuget.config"));

        IEnumerable<XElement> offenders = config.Descendants("packageSourceMapping")
            .Elements("packageSource")
            .Where(s => (string?)s.Attribute("key") != JauntyQSource)
            .Where(s => s.Elements("package").Any(p =>
                ((string?)p.Attribute("pattern"))?.StartsWith("JauntyQ", StringComparison.OrdinalIgnoreCase) == true));

        Assert.True(
            !offenders.Any(),
            $"Only '{JauntyQSource}' may map JauntyQ.*; also mapped by: "
                + string.Join(", ", offenders.Select(s => (string?)s.Attribute("key"))));
    }

    /// <summary>
    /// Every source ci.yml authenticates has to be declared by one of the two
    /// nuget.config files the workflow actually restores against.
    /// </summary>
    /// <remarks>
    /// Two, not one, since 2026-08-25. The skeleton-stamp job restores a product
    /// stamped from <c>skeleton/</c>, whose nuget.config is a generic product
    /// config and names its framework feed <c>framework-github</c> rather than
    /// this repository's <c>jauntyq-github</c>. Checking only the root config
    /// failed that job's auth step by name, which is the test working -- the fix
    /// is to widen what counts as declared, not to drop the check. Both files
    /// stay in scope, so authenticating a source neither declares is still a
    /// failure.
    /// </remarks>
    [Fact]
    public void Workflow_AuthenticatesTheSourceNameNuGetConfigActuallyDeclares()
    {
        string root = RepoRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        var declared = new[] { Path.Combine(root, "nuget.config"), Path.Combine(root, "skeleton", "nuget.config") }
            .Select(XDocument.Load)
            .SelectMany(c => c.Descendants("packageSources").Elements("add"))
            .Select(e => (string?)e.Attribute("key"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet(StringComparer.Ordinal);

        MatchCollection authenticated = UpdateSourcePattern().Matches(workflow);
        Assert.True(
            authenticated.Count > 0,
            "ci.yml no longer runs `dotnet nuget update source` — package-mode restore will 401.");

        foreach (Match match in authenticated)
        {
            string name = match.Groups[1].Value;
            Assert.True(
                declared.Contains(name),
                $"ci.yml authenticates package source '{name}', which nuget.config does not declare.");
        }
    }

    /// <summary>
    /// GitHub Packages requires auth even to read. A `permissions:` block that
    /// omits packages leaves GITHUB_TOKEN unable to read the feed, and the
    /// failure is a 401 during restore rather than anything naming permissions.
    /// </summary>
    [Fact]
    public void Workflow_GrantsThePackagesReadScopeTheRestoreNeeds()
    {
        string workflow = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));

        Assert.Matches(PackagesReadPattern(), workflow);
    }

    /// <summary>
    /// The sibling projects ci.yml drops are exactly the ones the solution
    /// declares.
    /// </summary>
    /// <remarks>
    /// NativeWeb.slnx lists the JauntyQ projects unconditionally, so that a
    /// local checkout can co-develop them; Directory.Build.props's
    /// ProjectReference is the conditional half, and the solution membership is
    /// not. A solution-wide restore therefore loads them in package mode too,
    /// which on a runner is MSB3202 before any of the feed wiring is reached.
    /// ci.yml removes them from the runner's copy by path, and a path is
    /// exactly the kind of thing that gets renamed on one side only.
    /// </remarks>
    [Fact]
    public void Workflow_DropsExactlyTheSiblingProjectsTheSolutionDeclares()
    {
        string root = RepoRoot();
        string solution = File.ReadAllText(Path.Combine(root, "NativeWeb.slnx"));
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        var declared = SolutionProjectPattern().Matches(solution)
            .Select(m => Normalize(m.Groups[1].Value))
            .Where(p => p.Contains("jauntyq/", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(
            declared.Count > 0,
            "NativeWeb.slnx no longer lists the JauntyQ projects — the ci.yml removal step should go too.");

        Match removal = SolutionRemovePattern().Match(workflow);
        Assert.True(removal.Success, "ci.yml no longer drops the JauntyQ projects from the solution.");

        var removed = removal.Groups[1].Value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(declared, removed);
    }

    /// <summary>
    /// ci.yml puts NativeWeb.slnx back before the first test step.
    /// </summary>
    /// <remarks>
    /// The removal step edits the runner's working copy, and the test above
    /// reads that same file to check the solution and the workflow still name
    /// the same projects. Without a restore it reads a solution the workflow
    /// has just emptied, so it asserts the opposite of what it means and fails
    /// on every run.
    /// <para>
    /// That is not hypothetical: it is what happened the first time CI got far
    /// enough to run the unit tier at all. Every earlier run died at restore or
    /// at compile, so the test had never once executed on a runner, and a test
    /// that has never run is not a passing test.
    /// </para>
    /// </remarks>
    [Fact]
    public void Workflow_RestoresTheSolutionBeforeTheFirstTestStep()
    {
        string workflow = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));

        int removal = workflow.IndexOf("dotnet sln NativeWeb.slnx remove", StringComparison.Ordinal);
        int restore = workflow.IndexOf("git checkout -- NativeWeb.slnx", StringComparison.Ordinal);
        int firstTest = workflow.IndexOf("dotnet test", StringComparison.Ordinal);

        Assert.True(removal >= 0, "ci.yml no longer drops the JauntyQ projects from the solution.");
        Assert.True(restore >= 0, "ci.yml empties NativeWeb.slnx of the JauntyQ projects and never puts it back.");
        Assert.True(firstTest >= 0, "ci.yml no longer runs any tests.");

        Assert.True(removal < restore, "ci.yml restores NativeWeb.slnx before the step that edits it.");
        Assert.True(restore < firstTest, "ci.yml runs tests against a NativeWeb.slnx it has emptied of JauntyQ projects.");
    }

    /// <summary>Compare paths, not separators: the solution writes them one way and the workflow the other.</summary>
    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/');

    [GeneratedRegex(@"dotnet nuget update source\s+(\S+)")]
    private static partial Regex UpdateSourcePattern();

    [GeneratedRegex(@"<Project\s+Path=""([^""]+)""")]
    private static partial Regex SolutionProjectPattern();

    // Bounded to the step's own continuation lines. An unanchored \s+\S+ runs
    // straight past the blank line and swallows the publish command's
    // apps/web/nativeweb.web.csproj, which then reads as a third project the
    // workflow claims to remove.
    [GeneratedRegex(@"dotnet sln NativeWeb\.slnx remove((?:\r?\n[ \t]+\S+)+)")]
    private static partial Regex SolutionRemovePattern();

    [GeneratedRegex(@"(?m)^\s*packages:\s*read\s*$")]
    private static partial Regex PackagesReadPattern();

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
