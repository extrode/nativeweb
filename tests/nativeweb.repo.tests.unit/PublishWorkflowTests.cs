using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// Publishing happens on a version tag and on nothing else (spec 028 R6).
/// </summary>
/// <remarks>
/// <para>
/// A version already on GitHub Packages cannot be replaced, only deleted and
/// re-pushed, which breaks every consumer that has restored it. So the cost of
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
public sealed class PublishWorkflowTests
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
    public void PublishHoldsWriteAccessToPackagesAndNothingMore()
    {
        string permissions = TopLevelBlock(
            File.ReadAllText(Path.Combine(RepoRoot(), PublishWorkflow)),
            "permissions");

        string[] granted = [.. permissions
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))];

        Assert.Equal(["contents: read", "packages: write"], granted);
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
