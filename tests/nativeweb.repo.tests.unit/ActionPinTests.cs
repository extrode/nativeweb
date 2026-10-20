using System.Globalization;
using System.Text.RegularExpressions;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// Third-party GitHub Actions are pinned to a commit SHA, never to a mutable tag.
/// </summary>
/// <remarks>
/// <para>
/// A <c>uses:</c> step runs someone else's code with the workflow's token on every push.
/// A tag is a pointer its owner can move, so <c>@v1</c> means "whatever that account
/// decides v1 is today" -- the executed code can change with no commit landing in this
/// repository and nothing to review. A SHA cannot move.
/// </para>
/// <para>
/// Covers the skeleton as well as this repo. The skeleton is a stamped product: a
/// weakness there is copied into every repository generated from it, and the person
/// running the generator is the least likely to audit a workflow they did not write.
/// </para>
/// <para>
/// <c>actions/*</c> is the deliberate exception. Those are published by GitHub itself,
/// from the same trust boundary already running the workflow, and pinning them by SHA
/// buys nothing against an attacker who by then already controls the runner. Every
/// other publisher is a separate trust boundary and gets a SHA.
/// </para>
/// </remarks>
public sealed partial class ActionPinTests
{
    public static TheoryData<string> WorkflowFiles()
    {
        var data = new TheoryData<string>();
        foreach (string file in EnumerateWorkflows())
        {
            data.Add(Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/'));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(WorkflowFiles))]
    public void ThirdPartyActions_ArePinnedToASha(string relativePath)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        foreach (string reference in References(text))
        {
            if (reference.StartsWith("actions/", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = reference.Split('@');
            Assert.True(parts.Length == 2, $"{relativePath}: `uses: {reference}` has no version at all.");
            Assert.True(
                ShaPattern().IsMatch(parts[1]),
                $"{relativePath}: `uses: {reference}` is pinned to a mutable tag. A third-party "
                    + "action runs with the workflow's token, so a moved tag swaps the executed "
                    + "code with nothing landing in this repo. Pin the 40-character commit SHA "
                    + "and keep the tag as a trailing comment.");
        }
    }

    // Guards the guard: if the glob stops finding workflows, every assertion above passes
    // vacuously and the pin is unprotected while the suite stays green.
    [Fact]
    public void TheScanFindsBothRepositoriesWorkflows()
    {
        string[] found = EnumerateWorkflows()
            .Select(f => Path.GetRelativePath(RepoRoot(), f).Replace('\\', '/'))
            .ToArray();

        Assert.Contains(found, f => f.StartsWith(".github/", StringComparison.Ordinal));
        Assert.Contains(found, f => f.StartsWith("skeleton/.github/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Neither repository uses a third-party action at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaced an "at least one is pinned" fact, which existed to stop the theory above
    /// looping over nothing. The last third-party action, <c>ilammy/msvc-dev-cmd</c>, was
    /// removed on 2026-08-20 once it was measured to be redundant — the ILCompiler locates
    /// MSVC itself — so that fact could no longer hold, and the honest replacement asserts the
    /// stronger property the repo now has rather than weakening the guard to keep it green.
    /// </para>
    /// <para>
    /// Zero is not "nothing to check": it is the state a reviewer should be told has changed.
    /// This failing is not a defect by itself — it means someone added a step that runs another
    /// account's code with the workflow's token, which is a decision to make deliberately. The
    /// SHA theory above then applies to whatever was added, and the regex behind both is
    /// exercised on synthetic documents by the <c>References_*</c> facts below, so neither is
    /// untested machinery while the count is zero.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoThirdPartyActionsAreUsed()
    {
        string[] thirdParty = EnumerateWorkflows()
            .SelectMany(f => References(File.ReadAllText(f)))
            .Where(r => !r.StartsWith("actions/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            thirdParty.Length == 0,
            $"Third-party action(s) added: {string.Join(", ", thirdParty)}. Each runs another "
                + "account's code with the workflow's token on every push. If that is intended, "
                + "pin the 40-character SHA (ThirdPartyActions_ArePinnedToASha enforces it) and "
                + "update this test to name what was added and why.");
    }

    /// <summary>
    /// The <c>actions/*</c> steps stay on a major whose <c>runs.using</c> is node24.
    /// </summary>
    /// <remarks>
    /// GitHub removed the node20 runtime from the runner, so a node20 action first draws a
    /// deprecation annotation on every run and then stops working. The first node24 major is
    /// <c>checkout@v5</c> and <c>setup-dotnet@v5</c> (both verified against each action's own
    /// action.yml, 2026-08-20); this repo runs v7 and v6 respectively. A floating major tag is
    /// deliberate here and consistent with the SHA exemption above -- these are GitHub's own
    /// actions, from the trust boundary already running the workflow.
    /// </remarks>
    [Theory]
    [MemberData(nameof(WorkflowFiles))]
    public void GitHubPublishedActions_AreOnANode24Major(string relativePath)
    {
        (string action, int firstNode24Major)[] floors =
        [
            ("actions/checkout", 5),
            ("actions/setup-dotnet", 5),
        ];

        string text = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        foreach (string reference in References(text))
        {
            foreach ((string action, int floor) in floors)
            {
                if (!reference.StartsWith(action + "@", StringComparison.Ordinal))
                {
                    continue;
                }

                string version = reference[(action.Length + 1)..];
                Match major = MajorPattern().Match(version);
                Assert.True(major.Success, $"{relativePath}: `uses: {reference}` has no readable major version.");
                Assert.True(
                    int.Parse(major.Groups["major"].Value, CultureInfo.InvariantCulture) >= floor,
                    $"{relativePath}: `uses: {reference}` runs on node20. The runner no longer "
                        + $"ships that runtime; {action}@v{floor} is the first node24 major.");
            }
        }
    }

    // A comment that merely names the key is not a step. Before this, writing "the uses: key"
    // in a workflow comment failed the theory with "`uses: ` has no version at all" -- which
    // fails in the safe direction but reports a defect in prose as a defect in the pin.
    [Theory]
    [InlineData("      # the uses: key must carry a SHA")]
    [InlineData("# uses: someone/action@v1")]
    [InlineData("  #- uses: someone/action@v1")]
    [InlineData("  # - uses: someone/action@v1")]
    public void References_IgnoresCommentedLines(string line)
    {
        Assert.Empty(References(line));
    }

    [Theory]
    [InlineData("      - uses: actions/checkout@v5", "actions/checkout@v5")]
    [InlineData("      uses: ilammy/msvc-dev-cmd@0b201ec", "ilammy/msvc-dev-cmd@0b201ec")]
    [InlineData("      - uses: owner/action@deadbeef # v3", "owner/action@deadbeef")]
    [InlineData("\t- uses: owner/action@deadbeef", "owner/action@deadbeef")]
    public void References_FindsRealSteps(string line, string expected)
    {
        Assert.Equal(expected, Assert.Single(References(line)));
    }

    // The two cases together on one document, because a workflow has both and the pattern has
    // to separate them in place rather than in isolation.
    [Fact]
    public void References_SeparatesStepsFromProseInOneDocument()
    {
        const string yaml = """
            jobs:
              build:
                steps:
                  # Every non-actions/* uses: entry below is pinned to a SHA.
                  - uses: actions/checkout@v5
                  - name: Set up MSVC
                    uses: ilammy/msvc-dev-cmd@0b201ec74fa43914dc39ae48a89fd1d8cb592756 # v1
            """;

        Assert.Equal(
            ["actions/checkout@v5", "ilammy/msvc-dev-cmd@0b201ec74fa43914dc39ae48a89fd1d8cb592756"],
            References(yaml));
    }

    /// <summary>
    /// Every <c>uses:</c> step reference in a workflow document, comments excluded.
    /// </summary>
    private static List<string> References(string yaml) =>
        [.. UsesPattern().Matches(yaml).Select(m => m.Groups["ref"].Value.Trim())];

    private static IEnumerable<string> EnumerateWorkflows()
    {
        string root = RepoRoot();
        foreach (string dir in new[]
                 {
                     Path.Combine(root, ".github", "workflows"),
                     Path.Combine(root, "skeleton", ".github", "workflows"),
                 })
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(dir, "*.yml").OrderBy(static f => f, StringComparer.Ordinal))
            {
                yield return file;
            }
        }
    }

    // Anchored to the start of a line, because an unanchored "uses:" matches inside a comment
    // and a workflow comment is prose, not a step. The optional "- " is the sequence marker on
    // the first key of a step; a later key ("uses:" under a "- name:") has none. Nothing may sit
    // between the line start and the key but whitespace and that marker, which is what excludes
    // "#".
    [GeneratedRegex(@"(?m)^[ \t]*(?:-[ \t]+)?uses:[ \t]*(?<ref>[^\s#]+)")]
    private static partial Regex UsesPattern();

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex ShaPattern();

    [GeneratedRegex(@"^v(?<major>\d+)")]
    private static partial Regex MajorPattern();

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
