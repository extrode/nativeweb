using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// Every vendored copy of a framework-owned asset is byte-identical to the
/// canonical file in <c>libs/nativeweb.assets/</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>libs/nativeweb.assets/README.md</c> has described this rule since the
/// directory existed: canonical here, byte-identical copies under each app's own
/// <c>wwwroot/</c>, and a test that fails on drift so the divergence never ships.
/// The test it describes was never in this tier. It lived beside <c>apps/web</c>
/// and left with the identity provider on 2026-08-31, along with
/// <c>SkeletonParityTests</c>, and for the same structural reason: both were
/// written against a twin that no longer exists.
/// </para>
/// <para>
/// This is the replacement, and it is deliberately in the repo tier rather than
/// in any one app's. Parity is a fact about the repository -- the whole point is
/// that N copies agree -- so a test that lives inside one of the copies can only
/// ever check itself against canonical, and says nothing when a second consumer
/// is added and forgotten. Adding a consumer here is one line in
/// <see cref="Consumers"/>, and forgetting to add it is the one failure mode this
/// cannot catch. <see cref="EveryConsumerWwwrootCssDirectoryIsDeclaredHere"/>
/// closes that: it walks the tree for css directories that look like consumers
/// and fails on any this file does not list.
/// </para>
/// <para>
/// Bytes, not text. A CRLF/LF normalisation or a re-encode with a BOM changes
/// what a browser is served and what a content hash resolves to, so a comparison
/// that reads both files as strings would pass while
/// <c>AssetUrls</c> fingerprints diverge.
/// </para>
/// </remarks>
public sealed class AssetParityTests
{
    /// <summary>
    /// The framework-owned stylesheets. <c>__product__.css</c> is absent on
    /// purpose: it is the product's own sheet, the one file in that directory a
    /// stamped product is meant to edit.
    /// </summary>
    private static readonly string[] CanonicalCss =
    [
        "tokens.css",
        "base.css",
        "components.css",
    ];

    /// <summary>
    /// Every directory holding a vendored copy, relative to the repository root.
    /// </summary>
    private static readonly string[] Consumers =
    [
        Path.Combine("skeleton", "apps", "__product__", "wwwroot", "css"),
        Path.Combine("apps", "sample", "wwwroot", "css"),
    ];

    public static TheoryData<string, string> Pairs()
    {
        TheoryData<string, string> data = [];
        foreach (string consumer in Consumers)
        {
            foreach (string file in CanonicalCss)
            {
                data.Add(consumer, file);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void VendoredCopyIsByteIdenticalToCanonical(string consumerDirectory, string fileName)
    {
        string root = RepoRoot();
        string canonical = Path.Combine(root, "libs", "nativeweb.assets", "css", fileName);
        string vendored = Path.Combine(root, consumerDirectory, fileName);

        Assert.True(File.Exists(canonical), $"Canonical asset missing: {canonical}");
        Assert.True(File.Exists(vendored), $"Vendored copy missing: {vendored}");

        byte[] canonicalBytes = File.ReadAllBytes(canonical);
        byte[] vendoredBytes = File.ReadAllBytes(vendored);

        Assert.True(
            canonicalBytes.AsSpan().SequenceEqual(vendoredBytes),
            $"{consumerDirectory}/{fileName} has drifted from libs/nativeweb.assets/css/{fileName}. "
                + "Edit the canonical file and re-copy it into every consumer in the same branch.");
    }

    /// <summary>
    /// A consumer that is added to the tree but not to <see cref="Consumers"/>
    /// would be unguarded and silent, which is the drift this file exists to
    /// stop. So the list is checked against the tree, not trusted.
    /// </summary>
    [Fact]
    public void EveryConsumerWwwrootCssDirectoryIsDeclaredHere()
    {
        string root = RepoRoot();
        string[] found = Directory
            .EnumerateDirectories(root, "css", SearchOption.AllDirectories)
            .Where(d => d.Replace('\\', '/').Contains("/wwwroot/css", StringComparison.Ordinal))
            .Where(d => !d.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal))
            .Where(d => !d.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal))
            .Where(d => !d.Replace('\\', '/').Contains("/tmp/", StringComparison.Ordinal))
            .Select(d => Path.GetRelativePath(root, d))
            .ToArray();

        string[] undeclared = found.Where(d => !Consumers.Contains(d)).ToArray();

        Assert.True(
            undeclared.Length == 0,
            "These wwwroot/css directories are not covered by the parity check. Add each to "
                + $"AssetParityTests.Consumers: {string.Join(", ", undeclared)}");
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
