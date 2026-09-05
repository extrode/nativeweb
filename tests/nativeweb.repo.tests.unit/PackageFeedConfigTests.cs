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
    /// Neither the solution nor the workflow carries the sibling-project workaround any more,
    /// and they have to lose it together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NativeWeb.slnx listed JauntyQ's two projects unconditionally, so a solution-wide restore
    /// loaded them even in package mode and a runner with no sibling checkout got MSB3202 before
    /// package mode was consulted. ci.yml worked around it by editing the runner's copy: a
    /// `dotnet sln remove` before restore and a `git checkout --` before the first test step,
    /// because the test that guarded the pair read the file the first step had just emptied.
    /// </para>
    /// <para>
    /// The 2026-09-05 split removed both members for real, which is what the removal step's own
    /// comment said to do. What can rot now is the reverse: an external project reintroduced to
    /// the solution without the surgery coming back, which is MSB3202 on a runner and nothing at
    /// all here. So this asserts the pairing rather than either half.
    /// </para>
    /// </remarks>
    [Fact]
    public void Workflow_DoesNoSolutionSurgeryAndTheSolutionNeedsNone()
    {
        string root = RepoRoot();
        string solution = File.ReadAllText(Path.Combine(root, "NativeWeb.slnx"));
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        string[] external = [.. SolutionProjectPattern().Matches(solution)
            .Select(m => Normalize(m.Groups[1].Value))
            .Where(p => p.StartsWith("..", StringComparison.Ordinal))];

        Assert.True(
            external.Length == 0,
            "NativeWeb.slnx declares a project outside this repository, which a runner cannot "
                + "restore: " + string.Join(", ", external));

        Assert.DoesNotContain("dotnet sln NativeWeb.slnx remove", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git checkout -- NativeWeb.slnx", workflow, StringComparison.Ordinal);
    }

    /// <summary>Compare paths, not separators: the solution writes them one way and the workflow the other.</summary>
    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/');

    [GeneratedRegex(@"dotnet nuget update source\s+(\S+)")]
    private static partial Regex UpdateSourcePattern();

    [GeneratedRegex(@"<Project\s+Path=""([^""]+)""")]
    private static partial Regex SolutionProjectPattern();

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
