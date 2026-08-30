using System.Text.RegularExpressions;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// No workflow or package config resolves anything through the old org.
/// </summary>
/// <remarks>
/// <para>
/// Both repositories moved from <c>extrode-legacy-org</c> to <c>extrode</c> on 2026-08-26.
/// GitHub redirects a transferred repository, so a stale <c>repository:</c> in a
/// checkout step keeps working and announces nothing; a package feed does not
/// get that courtesy, because a <c>GITHUB_TOKEN</c> is scoped to its own
/// repository and cannot read packages owned by another org at all. That
/// asymmetry is what made the move cost three days of red CI: the feed URL still
/// said <c>extrode-legacy-org</c> and every restore died NU1301 "403 (Forbidden)", while
/// four skeleton checkout steps pointing at the old org sat there silently
/// working and would have broken later, on whatever day the redirect lapsed.
/// </para>
/// <para>
/// Written the same way as the JauntyQRootProbeTests rule that refuses any probe
/// naming an org directory, and for the same reason: naming the new org here
/// would be the identical mistake with a fresher name, so the assertion is that
/// the OLD one is absent from anything that resolves.
/// </para>
/// <para>
/// Operative forms only -- a <c>repository:</c> value and a package-feed host.
/// Prose is deliberately allowed: ci.yml's permissions comment and
/// PackageFeedConfigTests both explain this outage by name, and a bare substring
/// ban would either fail on those explanations or force the history to be
/// deleted to keep the suite green.
/// </para>
/// </remarks>
public sealed partial class OrgReferenceTests
{
    private const string OldOrg = "extrode-legacy-org";

    [Fact]
    public void NoWorkflowChecksOutARepositoryFromTheOldOrg()
    {
        string root = RepoRoot();
        string[] workflows = WorkflowFiles(root);

        Assert.NotEmpty(workflows);

        string[] offenders = workflows
            .Where(p => RepositoryReferencePattern().IsMatch(File.ReadAllText(p)))
            .Select(p => Path.GetRelativePath(root, p))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"These workflows check out a repository from the retired '{OldOrg}' org: "
                + $"{string.Join(", ", offenders)}. They work only for as long as GitHub keeps "
                + "redirecting the transferred repositories.");
    }

    [Fact]
    public void NoPackageFeedPointsAtTheOldOrg()
    {
        string root = RepoRoot();
        string[] configs = NuGetConfigFiles(root);
        string[] candidates = [.. configs, .. WorkflowFiles(root)];

        string[] offenders = candidates
            .Where(p => FeedHostPattern().IsMatch(File.ReadAllText(p)))
            .Select(p => Path.GetRelativePath(root, p))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"These files resolve packages through the retired '{OldOrg}' org: "
                + $"{string.Join(", ", offenders)}. A GITHUB_TOKEN cannot read another org's "
                + "packages, so this fails on a runner with 403 and never fails locally.");
    }

    /// <summary>
    /// Both committed nuget.config files, named rather than discovered.
    /// </summary>
    /// <remarks>
    /// The skeleton one is here because it was NOT, and the omission cost the
    /// check its point: <c>skeleton/nuget.config</c> still pointed its
    /// <c>framework-github</c> source at <c>https://nuget.pkg.github.com/extrode-legacy-org</c>
    /// three days after the org move, with this very test green, because the
    /// candidate list was the root config plus the workflows. A guard that scans
    /// one of the two files it exists to scan reports on the file that was never
    /// going to rot.
    /// <para>
    /// Existence is asserted rather than filtered with <c>File.Exists</c>, for
    /// the reason WorkflowFiles gives: a silently-skipped file is how this
    /// passed while broken, and renaming either config should fail here loudly.
    /// </para>
    /// </remarks>
    private static string[] NuGetConfigFiles(string root)
    {
        string[] paths =
        [
            Path.Combine(root, "nuget.config"),
            Path.Combine(root, "skeleton", "nuget.config"),
        ];

        foreach (string path in paths)
        {
            Assert.True(
                File.Exists(path),
                $"Expected a nuget.config at {Path.GetRelativePath(root, path)}; this test names "
                    + "its files rather than discovering them, so a move must update it here.");
        }

        return paths;
    }

    /// <summary>
    /// The two workflow directories this repository owns, named rather than discovered.
    /// </summary>
    /// <remarks>
    /// A recursive walk from the repo root is the defect already logged against
    /// SdkPinTests: it descends into gitignored scratch, so a checkout parked
    /// under tmp/ -- exactly where AGENTS.md says scratch belongs -- turns this
    /// suite red over a file nobody committed. Both directories are asserted to
    /// exist, so this cannot quietly scan nothing if one is ever moved.
    /// </remarks>
    private static string[] WorkflowFiles(string root)
    {
        string[] dirs =
        [
            Path.Combine(root, ".github", "workflows"),
            Path.Combine(root, "skeleton", ".github", "workflows"),
        ];

        foreach (string dir in dirs)
        {
            Assert.True(
                Directory.Exists(dir),
                $"Expected workflows in {Path.GetRelativePath(root, dir)}; this test names its "
                    + "directories rather than discovering them, so a move must update it here.");
        }

        return [.. dirs.SelectMany(d => Directory.GetFiles(d, "*.yml"))];
    }

    /// <summary>A checkout step's repository value, not a mention of the org.</summary>
    [GeneratedRegex(@"(?m)^\s*repository:\s*extrode-legacy-org/", RegexOptions.IgnoreCase)]
    private static partial Regex RepositoryReferencePattern();

    /// <summary>A GitHub Packages feed host, not a mention of the org.</summary>
    [GeneratedRegex(@"nuget\.pkg\.github\.com/extrode-legacy-org", RegexOptions.IgnoreCase)]
    private static partial Regex FeedHostPattern();

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
