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
/// <para>
/// Since 2026-09-24 the root config declares nuget.org alone, and only the stamped product's
/// config names a GitHub source.
/// </para>
/// </remarks>
public sealed partial class PackageFeedConfigTests
{
    /// <summary>
    /// The root config declares nuget.org and nothing else.
    /// </summary>
    /// <remarks>
    /// The jauntyq-github source outlived every package this repository pinned from it, and
    /// still failed CI on 2026-09-24: <c>dotnet list package --vulnerable</c> queries every
    /// declared source, and the job's token got 403 (Forbidden) from that one.
    /// </remarks>
    [Fact]
    public void NuGetConfig_DeclaresNuGetOrgAlone()
    {
        XElement sources = XDocument.Load(Path.Combine(RepoRoot(), "nuget.config"))
            .Descendants("packageSources")
            .Single();

        Assert.Equal("clear", sources.Elements().First().Name.LocalName);
        Assert.Equal(
            ["nuget.org=https://api.nuget.org/v3/index.json"],
            sources.Elements("add").Select(a => $"{(string?)a.Attribute("key")}={(string?)a.Attribute("value")}"));
    }

    /// <summary>
    /// Every source ci.yml authenticates has to be declared by one of the two
    /// nuget.config files the workflow actually restores against.
    /// </summary>
    /// <remarks>
    /// Two, not one, since 2026-08-25. The skeleton-stamp job restores a product
    /// stamped from <c>skeleton/</c>, whose nuget.config is a generic product
    /// config and names its framework feed <c>framework-github</c> rather than
    /// this repository's <c>jauntyq-github</c> of the time. Checking only the root config
    /// failed that job's auth step by name, which is the test working -- the fix
    /// is to widen what counts as declared, not to drop the check. Both files
    /// stay in scope, so authenticating a source neither declares is still a
    /// failure. Since 2026-09-24 only the skeleton's declares one, and publish.yml
    /// authenticates nothing.
    /// </remarks>
    [Theory]
    [InlineData("ci.yml")]
    public void Workflow_AuthenticatesTheSourceNameNuGetConfigActuallyDeclares(string file)
    {
        string root = RepoRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", file));

        var declared = new[] { Path.Combine(root, "nuget.config"), Path.Combine(root, "skeleton", "nuget.config") }
            .Select(XDocument.Load)
            .SelectMany(c => c.Descendants("packageSources").Elements("add"))
            .Select(e => (string?)e.Attribute("key"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet(StringComparer.Ordinal);

        MatchCollection authenticated = AuthenticatedSourcePattern().Matches(workflow);
        Assert.True(
            authenticated.Count > 0,
            $"{file} no longer authenticates any package source — package-mode restore will 401.");

        foreach (Match match in authenticated)
        {
            string name = match.Groups[1].Value;
            Assert.True(
                declared.Contains(name),
                $"{file} authenticates package source '{name}', which nuget.config does not declare.");
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

        // "../" is the whole assertion, so the path is compared raw. Trimming leading dots to
        // normalise it -- which the helper this test replaced did -- makes the check vacuous.
        string[] external = [.. SolutionProjectPattern().Matches(solution)
            .Select(m => m.Groups[1].Value.Replace('\\', '/'))
            .Where(p => p.StartsWith("../", StringComparison.Ordinal))];

        Assert.True(
            external.Length == 0,
            "NativeWeb.slnx declares a project outside this repository, which a runner cannot "
                + "restore: " + string.Join(", ", external));

        Assert.DoesNotContain("dotnet sln NativeWeb.slnx remove", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git checkout -- NativeWeb.slnx", workflow, StringComparison.Ordinal);
    }

    // `add --name <source>`, not `update source <source>`: the credential moved out of the
    // checked-out config on 2026-09-05, and update cannot target a file that does not already
    // declare the source. The source name is still the thing being asserted.
    //
    // Anchored to the invocation rather than matching a bare `--name`, which any future step
    // could carry -- `gh release create --name`, for one -- and which would then be checked
    // against the declared package sources and fail naming nothing relevant.
    [GeneratedRegex(@"dotnet nuget add source .*\r?\n\s*--name\s+(\S+)")]
    private static partial Regex AuthenticatedSourcePattern();

    [GeneratedRegex(@"<Project\s+Path=""([^""]+)""")]
    private static partial Regex SolutionProjectPattern();

    [GeneratedRegex(@"(?m)^\s*packages:\s*read\s*$")]
    private static partial Regex PackagesReadPattern();

    /// <summary>
    /// A feed credential must never be written to a file inside the checkout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `dotnet nuget update source` with no --configfile writes to the nearest config that
    /// declares the source, which in this repository is the committed nuget.config. Three CI
    /// jobs and publish.yml did that, so a GITHUB_TOKEN -- including publish.yml's, which holds
    /// `packages: write` -- sat in the workspace in clear text for the rest of the job. Job
    /// scoped and ephemeral, and still the wrong file. Found reviewing spec 028, fixed 2026-09-05.
    /// </para>
    /// <para>
    /// It has to be `add` rather than `update`: `update source --configfile` on a file that does
    /// not already declare the source throws "Object reference not set to an instance of an
    /// object", which names nothing an operator could act on.
    /// </para>
    /// <para>
    /// The path is asserted to begin with a home-directory variable, not merely to differ from
    /// the literal "nuget.config". The first version of this test banned that one spelling and
    /// `--configfile NuGet.Config` passed it: a relative path resolves against the working
    /// directory, and on a case-insensitive filesystem that is the committed file this whole
    /// change exists to keep the token out of. Confirmed by mutation 2026-09-06.
    /// </para>
    /// <para>
    /// Creation is asserted too, because `add source --configfile` does not create the file it
    /// is given: it exits 1 with "File '...' does not exist." Both shells' forms were run
    /// against a scratch HOME and APPDATA on 2026-09-06 -- without the creation line, exit 1;
    /// with it, exit 0 and a config carrying the credential.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("ci.yml")]
    public void Workflow_WritesFeedCredentialsOutsideTheWorkspace(string file)
    {
        var checkedJobs = new List<string>();

        foreach ((string job, string body) in Jobs(file))
        {
            if (!body.Contains("--store-password-in-clear-text", StringComparison.Ordinal))
            {
                continue;
            }

            checkedJobs.Add(job);
            Assert.DoesNotContain("nuget update source", body, StringComparison.Ordinal);

            string[] commands = [.. AddSourceCommandPattern().Matches(body).Select(m => m.Value)];
            Assert.True(
                commands.Length > 0,
                $"{file} job '{job}' carries a feed credential that no `dotnet nuget add source` "
                    + "invocation claims.");

            foreach (string command in commands)
            {
                Match configfile = ConfigFilePattern().Match(command);
                Assert.True(
                    configfile.Success,
                    $"{file} job '{job}' runs `dotnet nuget add source` with no --configfile, so "
                        + "the credential lands in the nearest config that declares the source, "
                        + "which in this repository is the committed one.");

                string path = configfile.Groups[1].Value;
                Assert.Contains(path, UserConfigPaths);

                Assert.True(
                    CreatesDirectory(body, ParentOf(path)),
                    $"{file} job '{job}' writes '{path}' without creating its directory.");

                string written = WrittenConfigContent(body, path);
                Assert.True(
                    written.Length > 0,
                    $"{file} job '{job}' passes --configfile '{path}' without creating it when "
                        + "absent; `dotnet nuget add source` exits 1 rather than creating it.");

                var created = XDocument.Parse(written);
                Assert.Equal("configuration", created.Root?.Name.LocalName);
            }

            int credential = body.IndexOf("--store-password-in-clear-text", StringComparison.Ordinal);
            Match firstSdkCommand = SdkCommandPattern().Match(body);
            Assert.True(
                firstSdkCommand.Success && credential < firstSdkCommand.Index,
                $"{file} job '{job}' runs `{firstSdkCommand.Value}` before it authenticates the "
                    + "feed, which is a 401 on the runner and nothing at all here.");
        }

        Assert.NotEmpty(checkedJobs);
    }

    /// <summary>
    /// The one job that restores against a GitHub source authenticates, and no other job does.
    /// </summary>
    /// <remarks>
    /// The check above is satisfied by whatever credential steps remain, so deleting a whole
    /// step left the suite green: confirmed by mutation 2026-09-06, 194 tests and 0 failed with
    /// skeleton-stamp's step removed. A restore without the credential is a 401 on the runner
    /// and nothing at all here, so the count is anchored to the jobs that need one. Since
    /// 2026-09-24 that is skeleton-stamp alone: the root config declares nuget.org and nothing
    /// else, so a credential step anywhere else names a feed no config it restores against has.
    /// </remarks>
    [Theory]
    [InlineData("ci.yml", "skeleton-stamp")]
    [InlineData("publish.yml", "")]
    public void Workflow_CredentialsOnlyTheJobThatRestoresTheStampedProduct(string file, string job)
    {
        string[] expected = job.Length == 0 ? [] : [job];

        Assert.Equal(
            expected,
            Jobs(file)
                .Where(j => j.Body.Contains("--store-password-in-clear-text", StringComparison.Ordinal))
                .Select(j => j.Job));
    }

    /// <summary>
    /// The two paths NuGet reads a user-level config from, and the only two a credential may
    /// be written to.
    /// </summary>
    /// <remarks>
    /// Named in full rather than checked by prefix. `$HOME/` was the first attempt and it is
    /// not the property wanted: on ubuntu-latest the workspace is
    /// `/home/runner/work/&lt;repo&gt;/&lt;repo&gt;`, so `$HOME/work/nativeweb/nativeweb/nuget.config`
    /// passes a prefix check while naming the committed file this exists to protect.
    /// </remarks>
    private static readonly string[] UserConfigPaths =
    [
        "$HOME/.nuget/NuGet/NuGet.Config",
        @"$env:APPDATA\NuGet\NuGet.Config",
    ];

    private static string ParentOf(string path) =>
        path[..path.LastIndexOfAny(['/', '\\'])];

    private static bool CreatesDirectory(string job, string directory) =>
        job.Contains($"mkdir -p \"{directory}\"", StringComparison.Ordinal)
            || job.Contains($"-Path \"{directory}\"", StringComparison.Ordinal);

    /// <summary>
    /// The XML the job writes to <paramref name="path"/> when the file is absent, or empty if
    /// it writes nothing.
    /// </summary>
    /// <remarks>
    /// The content is returned rather than matched, so the caller can parse it. Asserting that
    /// a redirect exists says nothing about what lands: `&lt;configuration&gt;` unclosed reads the
    /// same to a text match and fails restore on the runner.
    /// </remarks>
    private static string WrittenConfigContent(string job, string path)
    {
        string escaped = Regex.Escape(path);

        Match redirect = Regex.Match(job, @"printf\s+'([^']*)'\s*>\s*""" + escaped + @"""");
        if (redirect.Success)
        {
            return redirect.Groups[1].Value.Replace(@"\n", "\n", StringComparison.Ordinal);
        }

        Match setContent = Regex.Match(
            job,
            @"Set-Content\s+-Path\s+""" + escaped + @"""\s+-Value\s+'([^']*)'");

        return setContent.Success ? setContent.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// The workflow's jobs, each with the text of its own block.
    /// </summary>
    /// <remarks>
    /// Splitting on a step boundary is what the first version did, and it is sensitive to where
    /// a comment sits: a comment above a step belongs to the previous chunk, so a token can be
    /// asserted against the wrong step. Job blocks are the two-space keys under `jobs:`, which
    /// is the last top-level key in both files, so that split is unambiguous.
    /// </remarks>
    private static List<(string Job, string Body)> Jobs(string file)
    {
        string workflow = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", file));

        // Matched rather than IndexOf'd on "\njobs:\n": a CRLF checkout, which is what git gives
        // a Windows clone by default, has no such substring and would fail every theory here
        // with "declares no jobs" rather than anything true.
        Match jobsKey = JobsKeyPattern().Match(workflow);
        Assert.True(jobsKey.Success, $"{file} declares no jobs: block.");

        MatchCollection ids = JobIdPattern().Matches(workflow, jobsKey.Index);
        Assert.True(ids.Count > 0, $"{file} declares a jobs: block with no jobs in it.");

        var jobs = new List<(string, string)>();
        for (int i = 0; i < ids.Count; i++)
        {
            int from = ids[i].Index;
            int to = i + 1 < ids.Count ? ids[i + 1].Index : workflow.Length;
            jobs.Add((ids[i].Groups[1].Value, WithoutComments(workflow[from..to])));
        }

        return jobs;
    }

    /// <summary>
    /// A job block with its comment lines dropped, YAML and shell alike.
    /// </summary>
    /// <remarks>
    /// The comments here explain the very things being asserted against -- why `update source`
    /// is not used, what `--configfile` does when its file is missing -- so a job read raw
    /// fails on its own documentation. Both YAML and both shells comment with `#`, so one rule
    /// covers all of it.
    /// </remarks>
    private static string WithoutComments(string job) => CommentLinePattern().Replace(job, string.Empty);

    [GeneratedRegex(@"(?m)^[ \t]*#.*$")]
    private static partial Regex CommentLinePattern();

    [GeneratedRegex(@"(?m)^jobs:[ \t]*$")]
    private static partial Regex JobsKeyPattern();

    [GeneratedRegex(@"(?m)^  ([A-Za-z0-9_-]+):[ \t]*$")]
    private static partial Regex JobIdPattern();

    // One `dotnet nuget add source` invocation, continuation lines and all -- bash joins with a
    // trailing backslash, pwsh with a backtick. Matching the whole command is what ties
    // --configfile to the invocation that uses it: collected job-wide, a --configfile moved onto
    // `dotnet restore` satisfied the assertion while `add source` fell back to the config
    // hierarchy in the workspace.
    [GeneratedRegex(@"dotnet nuget add source(?:[^\n]*[\\`]\r?\n)*[^\n]*")]
    private static partial Regex AddSourceCommandPattern();

    // The SDK commands whose restore needs the feed. `dotnet nuget` is deliberately absent: it
    // is the authentication itself, not something authentication has to precede.
    [GeneratedRegex(@"dotnet (?:restore|build|publish|pack|test|run)\b")]
    private static partial Regex SdkCommandPattern();

    [GeneratedRegex("--configfile\\s+\"?([^\"\\s]+)\"?")]
    private static partial Regex ConfigFilePattern();

    /// <summary>
    /// The two premium ids resolve from the same private source as JauntyQ.*, in the stamped
    /// product's config, by exact id. The root config dropped both with its private source on
    /// 2026-09-24.
    /// </summary>
    /// <remarks>
    /// Exact ids on purpose. The core Extrode.JauntyQ.* packages ship on nuget.org from
    /// 0.5.0, so an "Extrode.JauntyQ.*" prefix here would drag every core package onto the
    /// authenticated feed, where none of them exist, and restore would fail NU1101 on the
    /// generator. An exact id outranks any prefix, so only these two leave the catch-all.
    /// </remarks>
    [Theory]
    [InlineData("skeleton/nuget.config")]
    public void NuGetConfig_MapsThePremiumIdsToTheSourceThatServesJauntyQ(string file)
    {
        var config = XDocument.Load(Path.Combine(RepoRoot(), file));

        XElement jauntyQ = config.Descendants("packageSourceMapping")
            .Elements("packageSource")
            .Single(s => s.Elements("package").Any(p => (string?)p.Attribute("pattern") == "JauntyQ.*"));

        string[] patterns = jauntyQ.Elements("package")
            .Select(p => (string?)p.Attribute("pattern"))
            .OfType<string>()
            .ToArray();

        Assert.Contains("Extrode.JauntyQ.Cli.Premium", patterns);
        Assert.Contains("Extrode.JauntyQ.Schema.Contract", patterns);
        Assert.DoesNotContain(patterns, p => p.StartsWith("Extrode.JauntyQ.", StringComparison.Ordinal) && p.EndsWith('*'));
    }

    /// <summary>
    /// Every pinned JauntyQ package is a core Extrode.JauntyQ.* id that restores from
    /// nuget.org, never an id served only by the authenticated feed.
    /// </summary>
    /// <remarks>
    /// The pre-0.5.0 JauntyQ.* ids were deleted from that feed, and every CI restore
    /// from then until 2026-09-24 failed on them while a warm ~/.nuget/packages kept
    /// local builds green.
    /// </remarks>
    [Theory]
    [InlineData("Directory.Packages.props", "nuget.config")]
    [InlineData("skeleton/Directory.Packages.props", "skeleton/nuget.config")]
    public void JauntyQPins_ResolveFromNuGetOrg(string props, string file)
    {
        string[] pinned = XDocument.Load(Path.Combine(RepoRoot(), props))
            .Descendants("PackageVersion")
            .Select(p => (string?)p.Attribute("Include"))
            .OfType<string>()
            .Where(id => id.Contains("JauntyQ", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var config = XDocument.Load(Path.Combine(RepoRoot(), file));
        string[] Patterns(Func<string?, bool> key) => config
            .Descendants("packageSourceMapping")
            .Elements("packageSource")
            .Where(s => key((string?)s.Attribute("key")))
            .Elements("package")
            .Select(p => (string?)p.Attribute("pattern"))
            .OfType<string>()
            .ToArray();
        static bool Matches(string pattern, string id) => pattern.EndsWith('*')
            ? id.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
            : string.Equals(pattern, id, StringComparison.OrdinalIgnoreCase);

        string[] otherSources = [.. config.Descendants("packageSources").Elements("add")
            .Select(a => (string?)a.Attribute("key"))
            .OfType<string>()
            .Where(k => k != "nuget.org")];
        bool mapped = config.Descendants("packageSourceMapping").Any();
        Assert.True(
            mapped || otherSources.Length == 0,
            $"{file} declares {string.Join(", ", otherSources)} beside nuget.org with no packageSourceMapping.");

        string[] publicPatterns = mapped ? Patterns(k => k == "nuget.org") : ["*"];
        string[] privatePatterns = Patterns(k => k != "nuget.org");

        Assert.Equal(
            "https://api.nuget.org/v3/index.json",
            config.Descendants("packageSources").Elements("add")
                .Where(a => (string?)a.Attribute("key") == "nuget.org")
                .Select(a => (string?)a.Attribute("value"))
                .SingleOrDefault());
        Assert.NotEmpty(pinned);
        Assert.All(pinned, id =>
        {
            Assert.StartsWith("Extrode.JauntyQ.", id, StringComparison.Ordinal);
            Assert.Contains(publicPatterns, p => Matches(p, id));
            Assert.DoesNotContain(privatePatterns, p => Matches(p, id));
        });
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
