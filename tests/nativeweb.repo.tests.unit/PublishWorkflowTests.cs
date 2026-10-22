using System.Text.RegularExpressions;
using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// Publishing happens on a version tag and on nothing else (spec 028 R6).
/// </summary>
/// <remarks>
/// <para>
/// A version already on nuget.org can never be deleted or replaced, only
/// unlisted. So the cost of
/// an accidental publish is not a wasted run: it is a version number that has
/// to be burned. The trigger this guards against is <c>workflow_dispatch</c>,
/// which is the one someone adds while debugging the workflow and does not
/// remove.
/// </para>
/// <para>
/// Read as top-level YAML blocks rather than as substrings over the whole file.
/// A bare `Assert.Contains("tags:")` would pass on a workflow that also carried
/// `pull_request:`, and a bare `DoesNotContain("workflow_dispatch")` would fail
/// on a comment explaining why it is absent -- which the file has. The block
/// extraction below is what makes the question "which triggers are declared"
/// answerable instead of "does this string appear anywhere".
/// </para>
/// <para>
/// The tag patterns are read as well as the key that holds them. Asserting only
/// that <c>push</c> carries <c>tags</c> passes on <c>- "*"</c>, which publishes
/// on every tag including a moving one like <c>latest</c> -- the opposite of
/// what this class is named for.
/// </para>
/// </remarks>
public sealed partial class PublishWorkflowTests
{
    private const string PublishWorkflow = ".github/workflows/publish.yml";

    [Fact]
    public void PublishTriggersOnTagsAndNothingElse()
    {
        string on = TopLevelBlock(File.ReadAllText(Path.Combine(RepoRoot(), PublishWorkflow)), "on");

        string[] triggers = [.. Keys(on, indent: 2)];

        Assert.Equal(["push"], triggers);

        string[] pushKeys = [.. Keys(on, indent: 4)];

        Assert.Equal(["tags"], pushKeys);

        string[] patterns = [.. on
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line.TrimStart()[2..].Trim().Trim('"', '\''))];

        Assert.Equal(["v[0-9]+.[0-9]+.[0-9]+"], patterns);
    }

    [Fact]
    public void PublishHoldsReadAccessAndAnOidcTokenAndNothingMore()
    {
        string permissions = TopLevelBlock(
            File.ReadAllText(Path.Combine(RepoRoot(), PublishWorkflow)),
            "permissions");

        string[] granted = [.. permissions
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))];

        Assert.Equal(["contents: read", "id-token: write"], granted);
    }

    [Fact]
    public void PublishReadsNoStoredSecretButGitHubsOwnToken()
    {
        string[] secrets = [.. File
            .ReadAllLines(Path.Combine(RepoRoot(), PublishWorkflow))
            .Where(line => !line.TrimStart().StartsWith('#'))
            .SelectMany(line => SecretReference().Matches(line))
            .Select(match => match.Groups[1].Value)
            .Distinct()];

        Assert.Equal(["GITHUB_TOKEN"], secrets);
    }

    [Fact]
    public void TheCiGateReadsEveryCheckRunNotOnlyTheLatest()
    {
        string[] queries = [.. File
            .ReadAllLines(Path.Combine(RepoRoot(), PublishWorkflow))
            .Where(line => !line.TrimStart().StartsWith('#'))
            .Select(line => CheckRunsQuery().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)];

        string query = Assert.Single(queries);

        Assert.Contains("filter=all", query.Split('&'));
    }

    [Fact]
    public void CiRunsOnBranchPushesNotOnTheReleaseTag()
    {
        string on = TopLevelBlock(
            File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml")),
            "on");

        Assert.Equal(["pull_request", "push"], Keys(on, indent: 2));
        Assert.Equal(["branches"], Keys(on, indent: 4));
    }

    [Theory]
    [InlineData("${{ secrets.NUGET_API_KEY }}", "NUGET_API_KEY")]
    [InlineData("${{ secrets['NUGET_API_KEY'] }}", "NUGET_API_KEY")]
    [InlineData("${{ secrets[\"NUGET_API_KEY\"] }}", "NUGET_API_KEY")]
    public void SecretReference_ReadsEverySpelling(string line, string name) =>
        Assert.Equal(name, SecretReference().Match(line).Groups[1].Value);

    [Fact]
    public void ThePushSpendsAKeyMintedInTheSameStep()
    {
        string[] lines = [.. File.ReadAllLines(Path.Combine(RepoRoot(), PublishWorkflow))];

        int push = Array.FindIndex(lines, IsANuGetPush);

        Assert.True(push >= 0, "The workflow has no nuget push.");

        int step = Array.FindLastIndex(lines, push, line => line.TrimStart().StartsWith("- name:", StringComparison.Ordinal));

        Assert.True(step >= 0, "The nuget push sits in no named step.");

        int login = Array.FindIndex(lines, step, line => !line.TrimStart().StartsWith('#')
            && line.Contains("tools/nuget-login.ts", StringComparison.Ordinal));

        Assert.True(
            login > step && login < push,
            "The push step does not mint its own key with tools/nuget-login.ts before pushing.");
    }

    /// <summary>
    /// CI runs on every push and must never be able to publish.
    /// </summary>
    /// <remarks>
    /// This is the assertion that gives the trigger guard above its meaning. A
    /// tag-only publish workflow is worth nothing if <c>ci.yml</c> also pushes,
    /// because ci.yml runs on every branch -- and the tempting change is
    /// exactly that one, since ci.yml already packs (spec 028 R5) and the push
    /// is one more line.
    /// </remarks>
    [Fact]
    public void NoOtherWorkflowPushesAPackage()
    {
        string root = RepoRoot();

        string[] offenders = [.. Directory
            .GetFiles(Path.Combine(root, ".github", "workflows"))
            .Where(p => p.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .Where(p => Path.GetRelativePath(root, p).Replace('\\', '/') != PublishWorkflow)
            .Where(p => File.ReadAllLines(p).Any(IsANuGetPush))
            .Select(p => Path.GetRelativePath(root, p))];

        Assert.True(
            offenders.Length == 0,
            $"These workflows push a package: {string.Join(", ", offenders)}. Only "
                + $"{PublishWorkflow} may, because it is the only one whose trigger is a version "
                + "tag. Everything else runs on every push to every branch.");
    }

    [Fact]
    public void ThePushDoesNotSkipDuplicates()
    {
        string[] pushLines = [.. File
            .ReadAllLines(Path.Combine(RepoRoot(), PublishWorkflow))
            .SkipWhile(line => !IsANuGetPush(line))];

        Assert.NotEmpty(pushLines);

        string[] offenders = [.. pushLines
            .TakeWhile(line => line.Trim().Length > 0)
            .Where(line => !line.TrimStart().StartsWith('#'))
            .Where(line => line.Contains("--skip-duplicate", StringComparison.Ordinal))];

        Assert.True(
            offenders.Length == 0,
            "The push carries --skip-duplicate. A retagged release then publishes the packages "
                + "that failed the first time from a different commit than the ones that "
                + "succeeded, and the version ends up carrying two <repository commit> values. "
                + "Let the second attempt fail and delete the partial version by hand.");
    }

    [Fact]
    public void PublishRefusesACommitCiHasNotPassed()
    {
        string[] lines = [.. File.ReadAllLines(Path.Combine(RepoRoot(), PublishWorkflow))];

        int gate = Array.FindIndex(lines, line => line.Contains("check-runs", StringComparison.Ordinal)
            && !line.TrimStart().StartsWith('#'));
        int push = Array.FindIndex(lines, IsANuGetPush);

        Assert.True(
            gate >= 0,
            "Nothing in the workflow reads the tagged commit's check runs. A tag can be placed on "
                + "any commit, including one whose suite never went green, and the tag is the "
                + "only trigger this workflow has.");

        Assert.True(gate < push, "The CI check runs after the push, which gates nothing.");

        // The job keys in ci.yml, so a renamed job breaks this test rather than
        // leaving the gate reading a check run that no longer exists and always
        // finding nothing.
        string publish = string.Join('\n', lines);
        string ci = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));

        string[] jobs = [.. ci
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("jobs:", StringComparison.Ordinal))
            .Where(line => line.Length > 2
                && line.StartsWith("  ", StringComparison.Ordinal)
                && !char.IsWhiteSpace(line[2])
                && line.TrimEnd().EndsWith(':'))
            .Select(line => line.Trim().TrimEnd(':'))];

        Assert.NotEmpty(jobs);

        string[] ungated = [.. jobs.Where(job => !publish.Contains(job, StringComparison.Ordinal))];

        Assert.True(
            ungated.Length == 0,
            $"These ci.yml jobs are not required by the publish gate: {string.Join(", ", ungated)}. "
                + "A job the gate does not name can be red on the commit being released.");
    }

    /// <summary>
    /// A `dotnet nuget push` in an executable position, not in a comment.
    /// </summary>
    private static bool IsANuGetPush(string line)
    {
        string trimmed = line.TrimStart();

        return !trimmed.StartsWith('#')
            && trimmed.Contains("nuget push", StringComparison.Ordinal);
    }

    /// <summary>
    /// The body under a top-level YAML key, up to the next column-0 key.
    /// </summary>
    private static string TopLevelBlock(string yaml, string key)
    {
        string[] lines = yaml.Split('\n');
        int start = Array.FindIndex(lines, line => line.StartsWith($"{key}:", StringComparison.Ordinal));

        Assert.True(start >= 0, $"{PublishWorkflow} declares no top-level '{key}:'.");

        int end = start + 1;
        while (end < lines.Length
            && (lines[end].Length == 0 || char.IsWhiteSpace(lines[end][0]) || lines[end][0] == '#'))
        {
            end++;
        }

        return string.Join('\n', lines[(start + 1)..end]);
    }

    /// <summary>
    /// The mapping keys declared at one indent level inside a block.
    /// </summary>
    /// <remarks>
    /// Comments are dropped rather than parsed, which is what lets the workflow
    /// explain at length why <c>workflow_dispatch</c> and <c>pull_request</c>
    /// are absent without that explanation reading as a declaration.
    /// </remarks>
    private static IEnumerable<string> Keys(string block, int indent) =>
        block.Split('\n')
            .Where(line => line.Length > indent
                && line[..indent].All(char.IsWhiteSpace)
                && !char.IsWhiteSpace(line[indent])
                && line[indent] != '#'
                && line[indent] != '-'
                && line.Contains(':', StringComparison.Ordinal))
            .Select(line => line.Trim().Split(':')[0])
            .Order(StringComparer.Ordinal);

    [GeneratedRegex(@"secrets\s*(?:\.|\[\s*['""])([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex SecretReference();

    [GeneratedRegex(@"/check-runs(?:\?([^""\s]*))?")]
    private static partial Regex CheckRunsQuery();

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
