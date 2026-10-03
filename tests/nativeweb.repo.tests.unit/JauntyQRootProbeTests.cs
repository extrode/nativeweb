using System.Text.RegularExpressions;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// Where <c>Directory.Build.props</c> looks for the JauntyQ checkout, and what it must never
/// look for.
/// </summary>
/// <remarks>
/// <para>
/// Both probes used to name the legacy org's ancestor directory literally. On
/// 2026-08-26 that broke twice in a single move: the org directory was renamed to
/// <c>extrode.com</c> and jauntyq moved with it, so both probes and the fallback pointed at a
/// path that no longer existed. <c>EnsureJauntyQRoot</c> turned that into a clear error rather
/// than a silent skip, which is the one thing that went right -- but every local-checkout build
/// in the repository needed <c>-p:JauntyQRoot</c> until someone edited these lines.
/// </para>
/// <para>
/// A path arithmetic bug in a props file is invisible in review and invisible on the machine
/// that happens to have the old layout. These tests evaluate the probe expressions against a
/// synthetic directory tree instead of reading them, so the assertion is "this resolves to the
/// sibling checkout", not "this string looks right".
/// </para>
/// </remarks>
public sealed partial class JauntyQRootProbeTests
{
    /// <summary>No probe may name the directory the repositories happen to sit under today.</summary>
    /// <remarks>
    /// The whole point of the fix. A probe relative to the repository survives a rename of the
    /// org directory; one that spells the name out is a build break waiting on the next one.
    /// The pattern is deliberately not just the old org's name -- writing <c>extrode.com</c> in would be
    /// the identical mistake with a fresher name on it.
    /// </remarks>
    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData(@"skeleton\Directory.Build.props")]
    public void NoJauntyQProbe_NamesAnOrgDirectory(string relativePath)
    {
        foreach (string probe in JauntyQProbes(PropsText(relativePath)))
        {
            Assert.DoesNotMatch(OrgDirectoryPattern(), probe);
        }
    }

    /// <summary>The ordinary checkout: jauntyq beside this repository.</summary>
    [Fact]
    public void TheShallowProbe_ResolvesToASiblingOfTheRepository()
    {
        using var layout = new Layout();
        string repo = layout.Dir("org", "nativeweb");
        layout.Dir("org", "jauntyq");

        string[] resolved = ResolveAll("Directory.Build.props", repo);

        Assert.Contains(resolved, Directory.Exists);
        Assert.Equal(
            Path.GetFullPath(layout.Path("org", "jauntyq")),
            resolved.First(Directory.Exists));
    }

    /// <summary>
    /// The worktree checkout, two levels deeper, still reaches the same sibling.
    /// </summary>
    /// <remarks>
    /// Both worktree locations this repository uses sit exactly two directories below the repo
    /// root -- <c>.worktrees/&lt;branch&gt;</c> by hand and <c>.claude/worktrees/&lt;name&gt;</c>
    /// for the built-in tool -- so one probe covers both. Without it every worktree build fails
    /// at restore, which is how the second probe came to exist in the first place.
    /// </remarks>
    [Theory]
    [InlineData(".worktrees", "some-branch")]
    [InlineData(".claude", "worktrees")]
    public void TheDeepProbe_ReachesTheSameSiblingFromAWorktree(string first, string second)
    {
        using var layout = new Layout();
        string worktree = layout.Dir("org", "nativeweb", first, second);
        layout.Dir("org", "jauntyq");

        string[] resolved = ResolveAll("Directory.Build.props", worktree);

        Assert.Contains(resolved, Directory.Exists);
        Assert.Equal(
            Path.GetFullPath(layout.Path("org", "jauntyq")),
            resolved.First(Directory.Exists));
    }

    /// <summary>A stamped product finds jauntyq beside itself too.</summary>
    /// <remarks>
    /// The skeleton has a single probe, matching its own <c>NativeWebRoot</c>, which has always
    /// been the relative <c>..\NativeWeb</c>. The two roots now agree about where a framework
    /// checkout lives; before this they disagreed, and only one of them was right.
    /// </remarks>
    [Fact]
    public void TheSkeletonProbe_ResolvesToASiblingOfTheStampedProduct()
    {
        using var layout = new Layout();
        string product = layout.Dir("org", "someproduct");
        layout.Dir("org", "jauntyq");

        string[] resolved = ResolveAll(@"skeleton\Directory.Build.props", product);

        Assert.Contains(resolved, Directory.Exists);
        Assert.Equal(
            Path.GetFullPath(layout.Path("org", "jauntyq")),
            resolved.First(Directory.Exists));
    }

    /// <summary>
    /// The unguarded fallback assignment is one of the guarded probes, not a third path.
    /// </summary>
    /// <remarks>
    /// The fallback exists so <c>EnsureJauntyQRoot</c> can name a concrete directory in its
    /// error rather than an empty string. If it drifts away from the probes it names a place
    /// nothing ever looked, which is worse than saying nothing: it sends the reader to check a
    /// path the build never considered.
    /// </remarks>
    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData(@"skeleton\Directory.Build.props")]
    public void TheFallbackAssignment_IsOneOfTheProbedPaths(string relativePath)
    {
        string text = PropsText(relativePath);

        string[] guarded = JauntyQProbes(text).ToArray();
        string[] fallback = UnguardedJauntyQAssignments(text).ToArray();

        // The parse finding nothing must fail, not pass silently: an assertion loop over an
        // empty list is a green test that never reached the file.
        Assert.NotEmpty(guarded);
        Assert.Single(fallback);
        Assert.Contains(fallback[0], guarded, StringComparer.OrdinalIgnoreCase);
    }

    // ---- the skeleton's other root: the framework checkout ----

    /// <summary>No <c>NativeWebRoot</c> probe may name an org directory either.</summary>
    /// <remarks>
    /// Same rule, same reason. This probe was written before the JauntyQ one was fixed and never
    /// had the guard applied to it.
    /// </remarks>
    [Fact]
    public void NoNativeWebProbe_NamesAnOrgDirectory()
    {
        string[] probes = NativeWebProbes(PropsText(@"skeleton\Directory.Build.props")).ToArray();

        Assert.NotEmpty(probes);
        foreach (string probe in probes)
        {
            Assert.DoesNotMatch(OrgDirectoryPattern(), probe);
        }
    }

    /// <summary>
    /// A stamped product finds the framework beside itself whichever way the checkout is spelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The repository is <c>nativeweb</c> on disk, but <c>docs/getting-started.md</c> says to
    /// clone it as <c>NativeWeb</c> and the README draws it that way, so both spellings exist in
    /// the wild and a stamped product inherits whichever its author used. Windows hides this
    /// completely -- either probe matches either directory -- and the skeleton's own CI runs on
    /// Linux, where exactly one resolves.
    /// </para>
    /// <para>
    /// <b>This theory cannot fail on Windows</b>, and that is not a defect in it: a
    /// case-insensitive filesystem genuinely resolves a PascalCase-only probe against a lowercase
    /// directory, so on this machine there is no wrong answer to catch. Deleting one casing from
    /// the props file leaves it green here and red on Linux. What it does prove on every platform
    /// is the depth -- that the probe reaches a sibling of the stamped product and not some other
    /// level. <see cref="TheSkeletonNativeWebProbe_TriesBothCasings"/> is the half that fails
    /// anywhere.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("nativeweb")]
    [InlineData("NativeWeb")]
    public void TheSkeletonNativeWebProbe_ResolvesToASiblingWhicheverCasingIsOnDisk(string checkoutName)
    {
        using var layout = new Layout();
        string product = layout.Dir("org", "someproduct");
        layout.Dir("org", checkoutName);

        string[] resolved = ResolveAll(@"skeleton\Directory.Build.props", product, "NativeWebRoot");

        Assert.Contains(resolved, Directory.Exists);

        // Compared the way the filesystem itself compares. On Windows the lowercase probe matches
        // a `NativeWeb` directory and resolves to the string `...\nativeweb`, which is a different
        // string naming the same directory -- asserting Ordinal here would fail on the platform
        // where the code is correct, and pass for the wrong reason on the one where it matters.
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string expected = Path.GetFullPath(layout.Path("org", checkoutName));
        string actual = resolved.First(Directory.Exists);

        Assert.True(string.Equals(expected, actual, comparison), $"expected {expected}, got {actual}");
    }

    /// <summary>Both spellings of the framework checkout are probed, not just one.</summary>
    /// <remarks>
    /// The platform-independent half of the pair above. This asserts on the probe expressions
    /// themselves rather than on what a filesystem does with them, so deleting a casing fails it
    /// on Windows as well as on Linux -- which is the only way this repository can defend a
    /// Linux-only bug from a Windows machine.
    /// </remarks>
    [Fact]
    public void TheSkeletonNativeWebProbe_TriesBothCasings()
    {
        string[] probes = NativeWebProbes(PropsText(@"skeleton\Directory.Build.props")).ToArray();

        Assert.NotEmpty(probes);
        Assert.Contains(probes, p => p.EndsWith(@"..\nativeweb", StringComparison.Ordinal));
        Assert.Contains(probes, p => p.EndsWith(@"..\NativeWeb", StringComparison.Ordinal));
    }

    /// <summary>The framework probe's fallback is one of the paths it actually probed.</summary>
    [Fact]
    public void TheSkeletonNativeWebFallback_IsOneOfTheProbedPaths()
    {
        string text = PropsText(@"skeleton\Directory.Build.props");

        string[] guarded = Probes(text, "NativeWebRoot").ToArray();
        string[] fallback = UnguardedAssignments(text, "NativeWebRoot").ToArray();

        // A parse that finds nothing must fail rather than assert nothing.
        Assert.NotEmpty(guarded);
        Assert.Single(fallback);
        Assert.Contains(fallback[0], guarded, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every probe path expression for <c>JauntyQRoot</c>, guard included.</summary>
    private static IEnumerable<string> JauntyQProbes(string text) => Probes(text, "JauntyQRoot");

    /// <summary>Every probe path expression for <c>NativeWebRoot</c>, guard included.</summary>
    private static IEnumerable<string> NativeWebProbes(string text) => Probes(text, "NativeWebRoot");

    /// <summary>
    /// Every probe path expression for one root property: each element's assigned value, plus the
    /// path inside that element's own <c>Exists()</c> guard.
    /// </summary>
    /// <remarks>
    /// The guard is read out of the element's own condition rather than by scanning the file for
    /// <c>Exists(</c>. The skeleton props file declares both roots, so a file-wide scan hands
    /// NativeWeb's probe paths back as JauntyQ's and vice versa -- which is not merely untidy: it
    /// makes <see cref="NoJauntyQProbe_NamesAnOrgDirectory"/> pass or fail for the wrong file.
    /// </remarks>
    private static IEnumerable<string> Probes(string text, string property) =>
        RootElements(text, property)
            .SelectMany(m => new[] { m.Groups["value"].Value.Trim() }
                .Concat(ExistsGuardPattern().Matches(m.Groups["condition"].Value)
                    .Select(g => g.Groups["path"].Value.Trim())))
            // Ordinal, not OrdinalIgnoreCase: `..\nativeweb` and `..\NativeWeb` are two probes,
            // and collapsing them would hide exactly the thing
            // TheSkeletonNativeWebProbe_TriesBothCasings exists to assert.
            .Distinct(StringComparer.Ordinal);

    /// <summary>The assignments with no Exists() guard — the fallback, and nothing else.</summary>
    private static IEnumerable<string> UnguardedJauntyQAssignments(string text) =>
        UnguardedAssignments(text, "JauntyQRoot");

    private static IEnumerable<string> UnguardedAssignments(string text, string property) =>
        RootElements(text, property)
            .Where(m => !m.Groups["condition"].Value.Contains("Exists(", StringComparison.Ordinal))
            .Select(m => m.Groups["value"].Value.Trim());

    /// <summary>
    /// Every <c>&lt;property Condition="…"&gt;value&lt;/property&gt;</c> element in the file.
    /// Built per call rather than source-generated because the property name is the parameter.
    /// </summary>
    private static IEnumerable<Match> RootElements(string text, string property) =>
        Regex.Matches(
                text,
                $@"<{Regex.Escape(property)}\s+Condition=""(?<condition>[^""]*)""\s*>(?<value>[^<]*)</{Regex.Escape(property)}>")
            .Cast<Match>();

    /// <summary>
    /// Every probe expression, resolved as MSBuild would resolve it from
    /// <paramref name="propsDirectory"/>, in file order.
    /// </summary>
    /// <remarks>
    /// <c>$(MSBuildThisFileDirectory)</c> carries a trailing separator, which is why the
    /// substitution appends one. Getting that wrong shifts every probe by exactly one level and
    /// is the mistake this helper exists to make impossible to repeat by hand.
    /// </remarks>
    private static string[] ResolveAll(string relativePath, string propsDirectory) =>
        Resolve(JauntyQProbes(PropsText(relativePath)), propsDirectory);

    private static string[] ResolveAll(string relativePath, string propsDirectory, string property) =>
        Resolve(Probes(PropsText(relativePath), property), propsDirectory);

    private static string[] Resolve(IEnumerable<string> probes, string propsDirectory) =>
        probes
            .Select(p => p.Replace(
                "$(MSBuildThisFileDirectory)",
                propsDirectory + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .Select(p => Path.GetFullPath(p.Replace('\\', Path.DirectorySeparatorChar)))
            .ToArray();

    private static string PropsText(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('\\', Path.DirectorySeparatorChar)));

    /// <summary>A throwaway directory tree, removed whether the test passes or throws.</summary>
    private sealed class Layout : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("jqroot").FullName;

        public string Path(params string[] parts) =>
            System.IO.Path.Combine(new[] { this._root }.Concat(parts).ToArray());

        public string Dir(params string[] parts)
        {
            string path = this.Path(parts);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(this._root, recursive: true);
            }
            catch (IOException)
            {
                // A leaked temp directory is not a test failure.
            }
        }
    }

    // A <JauntyQRoot Condition="...">value</JauntyQRoot> element.
    // The path inside an Exists(...) guard, which is the probe the condition actually tests.
    [GeneratedRegex(@"Exists\('(?<path>[^']*)'\)")]
    private static partial Regex ExistsGuardPattern();

    // Any path segment that looks like the org directory of the day. `extrode.com` is listed
    // for the same reason the old org's directory was: naming it would be the same bug, freshly
    // made. The catch-all `.com` segment covers the old org's directory too.
    [GeneratedRegex(@"[\\/](extrode\.com|[^\\/]+\.com)[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex OrgDirectoryPattern();

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
