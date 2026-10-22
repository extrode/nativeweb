using System.Text.RegularExpressions;
using System.Xml.Linq;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The skeleton's CI resolves the framework from a checkout it clones itself, and every
/// piece of that wiring has to point at the same place.
/// </summary>
/// <remarks>
/// <para>
/// It used to force package mode (<c>-p:NativeWebUsePackages=true -p:JauntyQUsePackages=true</c>)
/// against a private feed that does not exist, so every run died at Restore and no later
/// step -- the CVE gate included -- ever executed. The workflow header said so and told the
/// reader not to remove the flags, because removing them alone would have left the build
/// probing for sibling checkouts a runner does not have. The fix is to provide the checkouts.
/// </para>
/// <para>
/// Only NativeWeb's, since 2026-09-05. JauntyQ split its generator and runtime out to a public
/// core repository, so cloning <c>extrode/jauntyq</c> stopped yielding either csproj a product
/// references; it resolves from packages now, and the clone step and <c>JauntyQRoot</c> went
/// with the change.
/// </para>
/// <para>
/// What can rot, all of it silent on a developer machine and fatal on a runner: the roots
/// and the clone paths drift apart, so <c>Ensure*Root</c> fails on a directory nothing
/// filled; a job gains a build step without the checkout steps; the roots go back to being
/// <c>-p:</c> flags, which reach the command they are written on and NOT
/// <c>dotnet list package</c> (no <c>-p:</c> at all) or the NativeAOT publish the e2e fixture
/// spawns as a child; or the framework moves back inside the product's directory, where the
/// product's <c>Directory.Packages.props</c> reaches it.
/// </para>
/// <para>
/// Measured 2026-08-17 against a product stamped from this skeleton: with the framework as a
/// sibling, correct roots build clean, passed either as <c>-p:</c> flags or as bare
/// environment variables; a bogus <c>NativeWebRoot</c> produces 10 "NativeWeb checkout not
/// found" errors rather than a silently-skipped ProjectReference; and the same clones nested
/// under the product instead fail NU1008 on both JauntyQ projects.
/// </para>
/// </remarks>
public sealed partial class SkeletonFrameworkResolutionTests
{
    /// <summary>
    /// The two roots are declared, and declared as environment variables.
    /// </summary>
    /// <remarks>
    /// Workflow-level <c>env:</c> is not a style choice here. MSBuild reads bare environment
    /// variables as properties, so one definition covers restore, build, test,
    /// <c>dotnet list package</c> and any child process; a <c>-p:</c> flag covers exactly the
    /// command it sits on. The old file needed four copies of the same setting for that
    /// reason, and four copies is three chances to update one of them.
    /// </remarks>
    [Fact]
    public void SkeletonCi_DeclaresBothFrameworkRootsAsWorkflowEnvironmentVariables()
    {
        string workflow = SkeletonWorkflow();

        string? root = WorkflowEnvValue(workflow, "NativeWebRoot");
        Assert.True(
            root is not null,
            "skeleton ci.yml declares no workflow-level NativeWebRoot; either the framework will "
                + "not resolve, or it resolves for only one of the two jobs.");
        Assert.StartsWith("${{ github.workspace }}/", root!, StringComparison.Ordinal);

        // JauntyQRoot is deliberately absent, and its absence is a fact worth pinning: JauntyQ
        // resolves from packages by default since the 2026-09-05 split, so a root reintroduced
        // here would point at a clone that no longer carries either referenced csproj.
        Assert.Null(WorkflowEnvValue(workflow, "JauntyQRoot"));
    }

    /// <summary>
    /// Each root points at the directory the workflow actually clones into.
    /// </summary>
    /// <remarks>
    /// The rename-one-side failure. <c>Ensure*Root</c> probes for a sentinel csproj, so a
    /// drifted path is a loud error rather than a silent skip -- but it is an error on every
    /// run of every stamped product, discovered by whoever pushed next rather than here.
    /// </remarks>
    [Fact]
    public void SkeletonCi_PointsEachRootAtAPathItActuallyChecksOut()
    {
        string workflow = SkeletonWorkflow();
        List<Checkout> checkouts = FrameworkCheckouts(workflow);

        Assert.True(checkouts.Count > 0, "skeleton ci.yml checks out no framework repository at all.");

        // Asserted rather than dereferenced: a missing root is the neighbouring test's
        // finding, and a NullReferenceException here would report it as a crash.
        string? value = WorkflowEnvValue(workflow, "NativeWebRoot");
        Assert.True(value is not null, "skeleton ci.yml declares no workflow-level NativeWebRoot.");

        string relative = value!.Replace("${{ github.workspace }}/", string.Empty, StringComparison.Ordinal);

        Assert.True(
            checkouts.Any(c => string.Equals(c.Path, relative, StringComparison.Ordinal)),
            $"NativeWebRoot resolves to '{relative}', which no checkout step writes to. Cloned instead: "
                + string.Join(", ", checkouts.Select(c => c.Path)));
    }

    /// <summary>
    /// Every job that builds clones the framework first.
    /// </summary>
    /// <remarks>
    /// <c>aot-publish</c> is the job this catches. It runs on a different runner with its own
    /// step list, so it needs its own checkout step; the workflow-level env var it inherits
    /// points at a directory that would otherwise be empty.
    /// </remarks>
    [Fact]
    public void EverySkeletonJobThatBuilds_ChecksOutTheFramework()
    {
        string workflow = SkeletonWorkflow();
        (string Name, string Body)[] jobs = Jobs(workflow).ToArray();

        // Anti-vacuity, and not a theoretical one. This test passed with the aot-publish
        // job's checkout steps deleted, because the perturbation rewrote the file with CRLF
        // endings and the job-header pattern's `$` would not step over the \r -- so Jobs()
        // returned nothing, the loop below never ran, and "found no jobs" read as "every job
        // is wired correctly". Same shape as the grep that SIGABRTed inside an `if` and
        // reported safe. A parse that finds nothing must fail, never pass.
        Assert.True(
            jobs.Length >= 2,
            $"parsed {jobs.Length} jobs out of skeleton ci.yml; the sweep below would assert nothing.");

        foreach ((string job, string body) in jobs)
        {
            if (!DotnetBuildStepPattern().IsMatch(body))
            {
                continue;
            }

            var paths = FrameworkCheckouts(body).Select(c => c.Path).ToHashSet(StringComparer.Ordinal);

            // One, not two: JauntyQ resolves from packages since the 2026-09-05 split and is
            // not cloned at all.
            Assert.True(
                paths.Count >= 1,
                $"job '{job}' runs a dotnet build/publish/test but checks out no framework "
                    + "repository. It needs NativeWeb, in its own step list.");
        }
    }

    /// <summary>Every framework checkout reads the public repository's release branch with the default token.</summary>
    /// <remarks>
    /// The framework repository is public, so actions/checkout's default GITHUB_TOKEN reads it
    /// and no repository secret is needed. <c>main</c> is its release branch; it publishes no
    /// tags yet.
    /// </remarks>
    [Fact]
    public void EveryFrameworkCheckout_ReadsMainWithTheDefaultToken()
    {
        string workflow = SkeletonWorkflow();
        List<Checkout> checkouts = FrameworkCheckouts(workflow);

        // A bare foreach over an empty list passes having asserted nothing -- the CRLF
        // failure mode again. One repository, in each of the two jobs; it was two of each
        // until JauntyQ stopped being cloned on 2026-09-05.
        Assert.Equal(2, checkouts.Count);

        foreach (Checkout checkout in checkouts)
        {
            Assert.Null(checkout.Token);
            Assert.Equal("main", checkout.Ref);
        }

        Assert.DoesNotContain("FRAMEWORK_CHECKOUT_TOKEN", workflow, StringComparison.Ordinal);
    }

    /// <summary>Package mode is available but must not be what CI depends on.</summary>
    /// <remarks>
    /// The feed carrying NativeWeb.* / JauntyQ.* does not exist. A flag reintroduced anywhere
    /// in a run step puts the whole workflow back to dying at Restore, which is the exact
    /// state this change ended. Comments are exempt: the header explains the history.
    /// </remarks>
    [Fact]
    public void NoSkeletonCiCommand_ForcesPackageMode()
    {
        foreach (string line in SkeletonWorkflow().Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            Assert.DoesNotContain("UsePackages=true", line, StringComparison.Ordinal);
            Assert.DoesNotContain("UsePackages: \"true\"", line, StringComparison.Ordinal);
        }
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

    /// <summary>
    /// The framework is checked out BESIDE the product, never inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The expensive one, and the reason the product is checked out into <c>product/</c>
    /// rather than at the workspace root. MSBuild and NuGet find
    /// <c>Directory.Packages.props</c> by walking up from each project, and the walk does not
    /// stop at a repository boundary -- there is no such thing. A framework clone nested
    /// under the product therefore inherits the PRODUCT's copy, which sets
    /// <c>ManagePackageVersionsCentrally=true</c>. jauntyq ships no
    /// <c>Directory.Packages.props</c> of its own and its PackageReferences carry Version
    /// attributes, so central package management switches on underneath it and restore dies.
    /// </para>
    /// <para>
    /// Measured 2026-08-17, both layouts, same clones: nested under the product gives NU1008
    /// on JauntyQ.Generator (eleven packages) and JauntyQ.Runtime (four). Siblings build
    /// clean. Nothing about the nesting is visible in the workflow's own text, which is why
    /// it is asserted here rather than left to a reviewer to notice.
    /// </para>
    /// </remarks>
    [Fact]
    public void SkeletonCi_ChecksOutTheFrameworkBesideTheProduct_NotInsideIt()
    {
        string workflow = SkeletonWorkflow();
        string? product = ProductCheckoutPath(workflow);

        Assert.True(
            product is not null,
            "skeleton ci.yml checks the product out at the workspace root; a framework clone "
                + "under it inherits the product's Directory.Packages.props and restore fails NU1008.");

        List<Checkout> checkouts = FrameworkCheckouts(workflow);
        Assert.True(checkouts.Count > 0, "skeleton ci.yml checks out no framework repository at all.");

        foreach (Checkout checkout in checkouts)
        {
            Assert.False(
                checkout.Path.StartsWith(product + "/", StringComparison.Ordinal)
                    || string.Equals(checkout.Path, product, StringComparison.Ordinal),
                $"'{checkout.Repository}' is cloned to '{checkout.Path}', inside the product at "
                    + $"'{product}'. It must be a sibling: nested, the product's "
                    + "Directory.Packages.props turns central package management on underneath it.");
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

    /// <summary>The framework token is not left behind in the clone's git config.</summary>
    /// <remarks>
    /// actions/checkout persists the credential it authenticated with into
    /// <c>.git/config</c> by default, so it stays readable for the rest of the job -- through
    /// the test steps, which execute the product's and the framework's code and the whole
    /// transitive package graph. Nothing in this workflow pushes to the framework, so there is
    /// nothing to trade for the exposure.
    /// </remarks>
    [Fact]
    public void NoFrameworkCheckout_LeavesItsCredentialInTheWorkspace()
    {
        List<Checkout> checkouts = FrameworkCheckouts(SkeletonWorkflow());
        Assert.Equal(2, checkouts.Count);

        foreach (Checkout checkout in checkouts)
        {
            Assert.Equal("false", checkout.PersistCredentials);
        }
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
