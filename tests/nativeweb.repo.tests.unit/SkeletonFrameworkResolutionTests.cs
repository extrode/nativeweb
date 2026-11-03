using System.Text.RegularExpressions;
using System.Xml.Linq;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The skeleton's CI resolves the framework the way a stamped product does by default: from the
/// pinned packages on nuget.org, with no framework checkout.
/// </summary>
/// <remarks>
/// <para>
/// It used to force package mode (<c>-p:NativeWebUsePackages=true -p:JauntyQUsePackages=true</c>)
/// against a private feed that did not exist, so every run died at Restore. It then cloned the
/// framework from <c>main</c> and built in checkout mode, which ran but tested the framework's
/// release branch on the day rather than the version the product pins.
/// </para>
/// <para>
/// JauntyQ moved to packages on 2026-09-05 and NativeWeb on 2026-11-04, once
/// <c>Extrode.NativeWeb.*</c> was on nuget.org. Package mode is the default in
/// <c>Directory.Build.props</c>, so the workflow selects nothing, and what can rot is a clone, a
/// root or a checkout-mode switch coming back.
/// </para>
/// </remarks>
public sealed partial class SkeletonFrameworkResolutionTests
{
    /// <summary>No job clones a framework repository.</summary>
    /// <remarks>
    /// A clone is only read in checkout mode, so one left behind is a step that does nothing until
    /// someone also flips the mode, and then the product's CI builds a framework branch instead of
    /// its pins.
    /// </remarks>
    [Fact]
    public void SkeletonCi_ChecksOutNoFrameworkRepository()
    {
        string workflow = SkeletonWorkflow();
        (string Name, string Body)[] jobs = [.. Jobs(workflow)];

        Assert.True(
            jobs.Length >= 2,
            $"parsed {jobs.Length} jobs out of skeleton ci.yml; the assertion below would assert nothing.");
        Assert.Empty(FrameworkCheckouts(workflow));
    }

    /// <summary>Neither framework root is declared, at any scope.</summary>
    [Fact]
    public void SkeletonCi_DeclaresNoFrameworkRoot()
    {
        string workflow = SkeletonWorkflow();

        Assert.Null(EnvValue(workflow, "NativeWebRoot"));
        Assert.Null(EnvValue(workflow, "JauntyQRoot"));
    }

    /// <summary>No step asks for checkout mode.</summary>
    /// <remarks>
    /// Checkout mode with no clone fails at <c>EnsureNativeWebRoot</c> on every run. Comments are
    /// exempt: the header explains how to opt in.
    /// </remarks>
    [Fact]
    public void NoSkeletonCiCommand_ForcesCheckoutMode()
    {
        string[] offenders = [.. SkeletonWorkflow()
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith('#'))
            .Where(line => line.Contains("UsePackages=false", StringComparison.Ordinal)
                || line.Contains("UsePackages: \"false\"", StringComparison.Ordinal)
                || line.Contains("UsePackages: false", StringComparison.Ordinal))
            .Select(line => line.Trim())];

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Nothing still expands the removed DOTNET_BUILD_ARGS.
    /// </summary>
    /// <remarks>
    /// An undefined shell variable expands to nothing and the step still exits 0, so a
    /// leftover reference is not a syntax error -- it is a build whose flags silently
    /// vanished. That is only harmless while the variable happens to carry nothing.
    /// </remarks>
    [Fact]
    public void NoSkeletonCiCommand_ExpandsAVariableTheWorkflowNoLongerDefines()
    {
        string workflow = SkeletonWorkflow();

        foreach (Match match in ShellVariablePattern().Matches(workflow))
        {
            string name = match.Groups[1].Value;

            // HOME is the runner's, not the workflow's. The credential step that wrote a token
            // under it is gone, but the assertion inverts rather than being skipped: a workflow
            // that sets HOME itself could point any later credential back inside the workspace.
            if (name == "HOME")
            {
                Assert.True(
                    EnvValue(workflow, name) is null,
                    "skeleton ci.yml defines HOME itself, which can point a credential written "
                        + "under $HOME back inside the checkout.");
                continue;
            }

            if (name.StartsWith("PG", StringComparison.Ordinal) || name == "out")
            {
                continue;
            }

            Assert.True(
                EnvValue(workflow, name) is not null,
                $"skeleton ci.yml expands ${name}, which it never defines -- it expands to nothing "
                    + "and the step still passes.");
        }
    }

    /// <summary>Every `run:` step is pointed at the product.</summary>
    /// <remarks>
    /// The other half of the sibling layout. The product is no longer at the workspace root,
    /// so without a working-directory default every `dotnet` and `bash db/migrate.sh` runs
    /// against an empty workspace -- loudly, but on every step of every stamped product.
    /// </remarks>
    [Fact]
    public void SkeletonCi_RunsEveryStepInTheProductDirectory()
    {
        string workflow = SkeletonWorkflow();
        string? product = ProductCheckoutPath(workflow);
        if (product is null)
        {
            return; // The test above owns this failure.
        }

        // Anchored to the workflow-level `defaults: run:` block, at its exact indent.
        // A bare "contains working-directory: product" passes when the line is moved onto a
        // single step or into ONE job's defaults, leaving the other job's run: steps
        // executing at the workspace root -- which is the drift this test exists to catch.
        Assert.Matches(
            new Regex(
                $@"(?m)^defaults:[ \t\r]*$\r?\n(?:[ \t]*#.*\r?\n)*  run:[ \t\r]*$\r?\n"
                    + $@"(?:[ \t]*#.*\r?\n)*    working-directory: {Regex.Escape(product)}[ \t\r]*$"),
            workflow);
    }

    /// <summary>No skeleton job authenticates to GitHub Packages.</summary>
    /// <remarks>
    /// The framework packages resolve from nuget.org, which needs no credential, so a step
    /// that registers a feed or a password is dead weight that still handles a secret.
    /// </remarks>
    [Fact]
    public void NoSkeletonJob_AuthenticatesToGitHubPackages()
    {
        string[] offenders = [.. SkeletonWorkflow()
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith('#'))
            .Where(line => FeedCredentialPattern().IsMatch(line))
            .Select(line => line.Trim())];

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("        run: dotnet nuget add source https://example/index.json -n feed")]
    [InlineData("        run: dotnet nuget sources add -n feed -s https://example/index.json")]
    [InlineData("        run: dotnet nuget update source feed -p x")]
    [InlineData("          TOKEN: ${{ secrets.FEED_PAT }}")]
    [InlineData("          TOKEN: ${{ secrets['FEED_PAT'] }}")]
    [InlineData("          SOURCE: https://nuget.pkg.github.com/extrode/index.json")]
    public void FeedCredentialPattern_MatchesEverySpelling(string line) =>
        Assert.Matches(FeedCredentialPattern(), line);

    [Theory]
    [InlineData("        run: dotnet restore")]
    [InlineData("          token: ${{ github.token }}")]
    public void FeedCredentialPattern_PassesAStepWithNoCredential(string line) =>
        Assert.DoesNotMatch(FeedCredentialPattern(), line);

    /// <summary>The skeleton ships a nuget.config, and its mapping is total.</summary>
    /// <remarks>
    /// <para>
    /// Without one, a stamped product's source list is whatever the machine has registered,
    /// and central package management turns "more than one source, incompletely mapped" into
    /// an error. Measured 2026-08-17 on a stamped product with no nuget.config, on a machine
    /// carrying one extra feed: NU1507 across all five projects, before a line was compiled.
    /// A GitHub runner has only nuget.org, so CI would never have shown it -- the failure
    /// lands on whoever stamps the product.
    /// </para>
    /// <para>
    /// The catch-all matters more than it looks: once a packageSourceMapping section exists,
    /// an id matching no pattern resolves from nowhere.
    /// </para>
    /// </remarks>
    [Fact]
    public void SkeletonNuGetConfig_ClearsMachineSourcesAndMapsEveryPackage()
    {
        var config = XDocument.Load(Path.Combine(SkeletonRoot(), "nuget.config"));

        Assert.True(
            config.Descendants("packageSources").Elements("clear").Any(),
            "skeleton nuget.config does not <clear /> machine-level sources; restore will resolve "
                + "differently on every machine.");

        XElement[] mapped = config.Descendants("packageSourceMapping").Elements("packageSource").ToArray();
        Assert.True(mapped.Length > 0, "skeleton nuget.config declares no packageSourceMapping.");
        Assert.Contains(
            mapped.SelectMany(s => s.Elements("package")),
            p => (string?)p.Attribute("pattern") == "*");
    }

    /// <summary>nuget.org is the only source the skeleton declares.</summary>
    /// <remarks>
    /// Since 2026-10-20 every pin, <c>Extrode.NativeWeb.*</c> and <c>Extrode.JauntyQ.*</c>, is
    /// published to nuget.org, so a stamped product restores with no credential. A second
    /// source would bring back a feed that needs one, and the dependency-confusion question
    /// of which source may claim which ids.
    /// </remarks>
    [Fact]
    public void SkeletonNuGetConfig_DeclaresNuGetOrgAlone()
    {
        var config = XDocument.Load(Path.Combine(SkeletonRoot(), "nuget.config"));

        string[] declared = config.Descendants("packageSources")
            .Elements("add")
            .Select(e => (string?)e.Attribute("key") ?? string.Empty)
            .ToArray();
        string[] mapped = config.Descendants("packageSourceMapping")
            .Elements("packageSource")
            .Select(s => (string?)s.Attribute("key") ?? string.Empty)
            .ToArray();

        Assert.Equal(["nuget.org"], declared);
        Assert.Equal(["nuget.org"], mapped);
    }

    private sealed record Checkout(string Repository, string Path, string? Ref, string? Token, string? PersistCredentials);

    /// <summary>
    /// The checkout steps that fetch another repository. The bare
    /// <c>- uses: actions/checkout@v7</c> that fetches the product itself names no repository
    /// and is deliberately excluded.
    /// </summary>
    private static List<Checkout> FrameworkCheckouts(string yaml) =>
        Steps(yaml)
            .Where(s => s.Contains("actions/checkout@", StringComparison.Ordinal))
            .Select(s => new Checkout(
                Scalar(s, "repository") ?? string.Empty,
                Scalar(s, "path") ?? string.Empty,
                Scalar(s, "ref"),
                Scalar(s, "token"),
                Scalar(s, "persist-credentials")))
            .Where(c => c.Repository.Length > 0)
            .ToList();

    /// <summary>
    /// Where the product itself is checked out, or null if it is left at the workspace root.
    /// </summary>
    /// <remarks>
    /// The product's step is the checkout that names no <c>repository:</c>. Null means the
    /// nesting problem is present, so callers treat it as a finding rather than a skip.
    /// </remarks>
    private static string? ProductCheckoutPath(string yaml) =>
        Steps(yaml)
            .Where(s => s.Contains("actions/checkout@", StringComparison.Ordinal))
            .Where(s => Scalar(s, "repository") is null)
            .Select(s => Scalar(s, "path"))
            .FirstOrDefault(p => p is not null);

    /// <summary>Split on step boundaries: a "- " at the indent GitHub Actions uses for steps.</summary>
    private static IEnumerable<string> Steps(string yaml) =>
        StepBoundaryPattern().Split(yaml).Skip(1);

    /// <summary>Top-level jobs, as (name, body) — a job's body runs to the next 2-space key.</summary>
    private static IEnumerable<(string Name, string Body)> Jobs(string yaml)
    {
        int jobsAt = yaml.IndexOf("\njobs:", StringComparison.Ordinal);
        if (jobsAt < 0)
        {
            yield break;
        }

        MatchCollection headers = JobHeaderPattern().Matches(yaml, jobsAt);
        for (int i = 0; i < headers.Count; i++)
        {
            int start = headers[i].Index + headers[i].Length;
            int end = i + 1 < headers.Count ? headers[i + 1].Index : yaml.Length;
            yield return (headers[i].Groups[1].Value, yaml[start..end]);
        }
    }

    private static string? Scalar(string block, string key) =>
        Regex.Match(block, $@"(?m)^\s*{Regex.Escape(key)}:\s*(\S.*?)\s*$") is { Success: true } m
            ? m.Groups[1].Value
            : null;

    /// <summary>An <c>env:</c> entry anywhere in the file, at any scope.</summary>
    private static string? EnvValue(string yaml, string name) => Scalar(yaml, name);

    /// <summary>
    /// An <c>env:</c> entry in the WORKFLOW-level block, i.e. above <c>jobs:</c>.
    /// </summary>
    /// <remarks>
    /// The scope is the point. A root moved into one job's (or one step's) <c>env:</c> still
    /// reads as defined while the other job builds without it -- and the NativeWeb half of
    /// that drift would not even go red, because the skeleton's fallback probe
    /// (<c>Directory.Build.props</c>, one directory up from the product) happens to resolve to
    /// the sibling this layout puts there. A test that cannot tell "defined for every job"
    /// from "defined for one" is not testing what its name claims.
    /// </remarks>
    private static string? WorkflowEnvValue(string yaml, string name)
    {
        int jobsAt = yaml.IndexOf("\njobs:", StringComparison.Ordinal);
        return Scalar(jobsAt < 0 ? yaml : yaml[..jobsAt], name);
    }

    private static string SkeletonWorkflow() =>
        File.ReadAllText(Path.Combine(SkeletonRoot(), ".github", "workflows", "ci.yml"));

    private static string SkeletonRoot() => Path.Combine(RepoRoot(), "skeleton");

    [GeneratedRegex(@"(?m)^      - ")]
    private static partial Regex StepBoundaryPattern();

    // The \r is load-bearing. .NET's multiline `$` matches before the \n but does not step
    // over a preceding \r, so on a CRLF checkout -- which is one core.autocrlf setting away --
    // `[ \t]*$` matches no job header at all and every sweep over Jobs() silently asserts
    // nothing. Measured: a CRLF copy of this workflow parsed 0 jobs where an LF copy parsed 2.
    [GeneratedRegex(@"(?m)^  ([A-Za-z][\w-]*):[ \t\r]*$")]
    private static partial Regex JobHeaderPattern();

    // dotnet verbs that evaluate the project graph, and so need the roots resolved.
    [GeneratedRegex(@"dotnet\s+(restore|build|test|publish|list)\b")]
    private static partial Regex DotnetBuildStepPattern();

    // A $VAR or ${VAR} expansion in a run: step. Excludes ${{ ... }}, which is GitHub's own
    // expression syntax and is substituted before the shell ever sees it.
    [GeneratedRegex(@"\$(?!\{\{)\{?([A-Za-z_][A-Za-z_0-9]*)\}?")]
    private static partial Regex ShellVariablePattern();

    // A feed registration in either CLI spelling, a GitHub Packages host, or any stored
    // secret in dotted or indexed form. GITHUB_TOKEN reaches checkout as github.token.
    [GeneratedRegex(@"dotnet\s+nuget\s+(add\s+source|sources?\s+add|update\s+source)|nuget\.pkg\.github\.com|secrets\s*(\.|\[)")]
    private static partial Regex FeedCredentialPattern();

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
