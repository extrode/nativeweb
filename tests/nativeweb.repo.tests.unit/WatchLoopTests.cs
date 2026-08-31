using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The edit-and-see-it loop the quick start promises still has the two pieces it
/// needs.
/// </summary>
/// <remarks>
/// <para>
/// Measured on 2026-08-31, both halves. Templates are <c>AdditionalFiles</c>, and
/// <c>dotnet watch</c>'s default globs are the compile items, so without a
/// matching <c>Watch</c> item an edit to a template produced no file-change line
/// at all and the running app went on serving the previous markup. And under the
/// default hot-reload session this project detected nothing whatever -- template
/// or <c>.cs</c> -- while a scratch console app in the same tree rebuilt on both,
/// which is why the documented command carries <c>--no-hot-reload</c>.
/// </para>
/// <para>
/// Neither fact can be caught by any suite that runs the application: a watcher
/// that never fires leaves every request answering exactly as before. The failure
/// mode is a developer editing a template, seeing no change, and concluding the
/// render layer caches -- so the guard has to be on the declaration, in this tier,
/// where a future csproj tidy-up that drops an "unused" item group fails here
/// rather than in someone's afternoon.
/// </para>
/// </remarks>
public sealed class WatchLoopTests
{
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

    [Theory]
    [InlineData(@"<Watch Include=""templates\**\*.html"" />")]
    [InlineData(@"<Watch Include=""locales\*.json"" />")]
    public void SampleProjectWatchesEveryGeneratorInputTheQuickStartSaysYouCanEdit(string item)
    {
        string csproj = File.ReadAllText(
            Path.Combine(RepoRoot(), "apps", "sample", "nativeweb.sample.csproj"));

        Assert.Contains(item, csproj, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every <c>AdditionalFiles</c> glob under the project's own directory has a
    /// <c>Watch</c> glob to match.
    /// </summary>
    /// <remarks>
    /// The two <see cref="InlineData"/> cases above name today's inputs; this one
    /// is what catches tomorrow's. A third generator input added as an
    /// <c>AdditionalFiles</c> line and not as a <c>Watch</c> line is the same
    /// silent failure, and nobody would think to add a theory case for it. The
    /// <c>..\..\db\</c> globs are excluded because they point outside the project
    /// and are schema and SQL rather than markup: editing those is a build, not a
    /// page refresh.
    /// </remarks>
    [Fact]
    public void EveryLocalAdditionalFilesGlobHasAMatchingWatchGlob()
    {
        string csproj = File.ReadAllText(
            Path.Combine(RepoRoot(), "apps", "sample", "nativeweb.sample.csproj"));

        var additional = Globs(csproj, "AdditionalFiles")
            .Where(g => !g.StartsWith("..", StringComparison.Ordinal))
            .ToList();
        var watched = Globs(csproj, "Watch").ToList();

        Assert.NotEmpty(additional);
        Assert.Empty(additional.Except(watched, StringComparer.Ordinal));
    }

    private static IEnumerable<string> Globs(string csproj, string itemName)
    {
        const string open = @" Include=""";
        foreach (string line in csproj.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith($"<{itemName}{open}", StringComparison.Ordinal))
            {
                continue;
            }

            int start = trimmed.IndexOf(open, StringComparison.Ordinal) + open.Length;
            int end = trimmed.IndexOf('"', start);
            yield return trimmed[start..end];
        }
    }
}
