using System.Text.RegularExpressions;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The framework's own assets are delivered from one place, and the third-party
/// trees each app vendors for itself agree with each other.
/// </summary>
/// <remarks>
/// <para>
/// The first half used to be a parity check, because vendoring by hand was how a
/// framework asset reached an app: canonical in <c>libs/nativeweb.assets/</c>,
/// byte-identical copies under each <c>wwwroot/</c>, drift caught here. Spec 015
/// R7 removed the copies as a mechanism — the files moved to
/// <c>libs/nativeweb.web/wwwroot/</c> and an MSBuild <c>.targets</c> contributes
/// them to every consumer's static web assets, so there is one copy and nothing
/// to keep in sync. What survives is the guard on the leftovers, which is a
/// transitional fact, and the third-party check, which is not.
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
public sealed partial class AssetParityTests
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

    /// <summary>
    /// The framework-owned scripts, keyed by their path under each consumer's
    /// <c>wwwroot/</c>.
    /// </summary>
    /// <remarks>
    /// Added 2026-08-31. The css guard had been read as covering the design system,
    /// and it does; what it never covered is the JavaScript the layout loads
    /// alongside it, which drifts by the identical mechanism and is the half that
    /// can change behaviour rather than appearance.
    /// </remarks>
    private static readonly string[] CanonicalScripts =
    [
        Path.Combine("vendor", "nativeweb", "boot.js"),
    ];

    /// <summary>
    /// The consumers' <c>wwwroot/</c> roots, for the checks that compare vendored
    /// trees against each other rather than against a canonical file.
    /// </summary>
    private static readonly string[] ConsumerRoots =
    [
        Path.Combine("skeleton", "apps", "__product__", "wwwroot"),
        Path.Combine("apps", "sample", "wwwroot"),
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

    /// <summary>
    /// Where a framework-owned asset lives now: one copy, delivered to every
    /// consumer by <c>libs/nativeweb.web/build/NativeWeb.Web.targets</c> (spec
    /// 015 R7) and served under <c>/_nativeweb/</c>.
    /// </summary>
    private static string DeliveredAsset(string root, params string[] segments) =>
        Path.Combine([root, "libs", "nativeweb.web", "wwwroot", "_nativeweb", .. segments]);

    /// <summary>
    /// The four files the framework delivers are present where the
    /// <c>.targets</c> globs for them.
    /// </summary>
    /// <remarks>
    /// The glob is <c>wwwroot\**\*</c>, so a file that goes missing produces no
    /// build error at all — the consumer loses a stylesheet and finds out in a
    /// browser. This is the fact that turns that into a failing test.
    /// </remarks>
    [Fact]
    public void TheFrameworkDeliversEveryAssetItOwns()
    {
        string root = RepoRoot();
        foreach (string file in CanonicalCss)
        {
            Assert.True(File.Exists(DeliveredAsset(root, "css", file)), $"Missing: {DeliveredAsset(root, "css", file)}");
        }

        Assert.True(File.Exists(DeliveredAsset(root, "boot.js")), $"Missing: {DeliveredAsset(root, "boot.js")}");
    }

    /// <summary>
    /// A consumer's leftover copy of a framework-owned stylesheet has not
    /// drifted from the delivered one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until 2026-08-31 this compared each consumer's copy against a canonical
    /// file in <c>libs/nativeweb.assets/css/</c>, and a missing copy was a
    /// failure: vendoring by hand was the delivery mechanism. Spec 015 R7
    /// replaced it — the file is delivered by the build, the templates resolve
    /// it at <c>/_nativeweb/css/</c>, and the copies still sitting in each
    /// consumer's <c>wwwroot/css/</c> are unreferenced leftovers awaiting
    /// <c>scripts/cleanup/015-framework-web-seam.ps1</c>.
    /// </para>
    /// <para>
    /// So absence passes and presence is checked. Not a weakened assertion: a
    /// leftover that drifts is a file being edited by someone who thinks it is
    /// still live, which is the exact confusion the removal is for.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void ALeftoverVendoredCopyHasNotDriftedFromTheDeliveredAsset(string consumerDirectory, string fileName)
    {
        string root = RepoRoot();
        string delivered = DeliveredAsset(root, "css", fileName);
        string vendored = Path.Combine(root, consumerDirectory, fileName);

        Assert.True(File.Exists(delivered), $"Delivered asset missing: {delivered}");
        if (!File.Exists(vendored))
        {
            return;
        }

        Assert.True(
            File.ReadAllBytes(delivered).AsSpan().SequenceEqual(File.ReadAllBytes(vendored)),
            $"{consumerDirectory}/{fileName} differs from the delivered "
                + $"libs/nativeweb.web/wwwroot/_nativeweb/css/{fileName}, and nothing serves it: the "
                + "framework's copy is the one every consumer gets. Delete the leftover rather than editing it.");
    }

    public static TheoryData<string, string> ScriptPairs()
    {
        TheoryData<string, string> data = [];
        foreach (string consumer in ConsumerRoots)
        {
            foreach (string file in CanonicalScripts)
            {
                data.Add(consumer, file);
            }
        }

        return data;
    }

    /// <summary>
    /// The script half of
    /// <see cref="ALeftoverVendoredCopyHasNotDriftedFromTheDeliveredAsset"/>,
    /// with the same absence-passes rule and for the same reason.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScriptPairs))]
    public void ALeftoverVendoredScriptHasNotDriftedFromTheDeliveredAsset(string consumerRoot, string relativePath)
    {
        string root = RepoRoot();
        string delivered = DeliveredAsset(root, Path.GetFileName(relativePath));
        string vendored = Path.Combine(root, consumerRoot, relativePath);

        Assert.True(File.Exists(delivered), $"Delivered asset missing: {delivered}");
        if (!File.Exists(vendored))
        {
            return;
        }

        Assert.True(
            File.ReadAllBytes(delivered).AsSpan().SequenceEqual(File.ReadAllBytes(vendored)),
            $"{consumerRoot}/{relativePath} differs from the delivered "
                + $"libs/nativeweb.web/wwwroot/_nativeweb/{Path.GetFileName(relativePath)}, and nothing serves "
                + "it. Delete the leftover rather than editing it.");
    }

    /// <summary>
    /// The third-party vendored trees — htmx, Alpine, the fonts — agree between
    /// consumers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These have no canonical copy in <c>libs/nativeweb.assets</c>, because they
    /// are not ours to own: what is vendored is an upstream release, and the
    /// framework's claim about them is only that every consumer is on the same one.
    /// So the guard is consumer-against-consumer, on names and bytes both.
    /// </para>
    /// <para>
    /// They agreed already when this was written. That is the point at which to
    /// write the check — an upgrade applied to one tree and not the other is a
    /// stamped product on a different htmx from the reference application, and the
    /// symptom of that is a behaviour difference nobody traces back to a file.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThirdPartyVendorTreesAgreeAcrossConsumers()
    {
        string root = RepoRoot();
        string[] roots = ConsumerRoots.Select(c => Path.Combine(root, c, "vendor")).ToArray();

        foreach (string dir in roots)
        {
            Assert.True(Directory.Exists(dir), $"Vendor directory missing: {dir}");
        }

        // nativeweb/ is ours and is covered above; everything else here is upstream.
        static string[] Names(string dir) => Directory
            .EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("nativeweb/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] first = Names(roots[0]);
        foreach (string other in roots.Skip(1))
        {
            string[] second = Names(other);

            Assert.True(
                first.SequenceEqual(second, StringComparer.Ordinal),
                $"Vendored file lists differ between {roots[0]} and {other}: "
                    + $"only in the first: {string.Join(", ", first.Except(second, StringComparer.Ordinal))}; "
                    + $"only in the second: {string.Join(", ", second.Except(first, StringComparer.Ordinal))}");

            foreach (string name in first)
            {
                Assert.True(
                    File.ReadAllBytes(Path.Combine(roots[0], name)).AsSpan()
                        .SequenceEqual(File.ReadAllBytes(Path.Combine(other, name))),
                    $"vendor/{name} differs between consumers. An upstream upgrade has to be "
                        + "applied to every vendored tree in the same branch.");
            }
        }
    }

    /// <summary>
    /// Every <c>/_nativeweb/</c> URL a consumer's markup references is a file the
    /// framework actually delivers.
    /// </summary>
    /// <remarks>
    /// The failure this catches has no build error in front of it. The
    /// <c>.targets</c> globs <c>wwwroot\**\*</c>, so a template asking for a file
    /// the library does not ship compiles, links and publishes; what happens is
    /// <c>AssetUrls.Resolve</c> throwing on the first render, or — in the
    /// skeleton, whose markup no local build compiles — nothing at all until
    /// somebody stamps a product.
    /// </remarks>
    [Fact]
    public void EveryFrameworkAssetUrlInConsumerMarkupIsDelivered()
    {
        string root = RepoRoot();
        string[] sources =
        [
            .. Directory.EnumerateFiles(Path.Combine(root, "apps", "sample"), "*.html", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(Path.Combine(root, "apps", "sample"), "*.cs", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(
                Path.Combine(root, "skeleton", "apps", "__product__", "templates"), "*.html", SearchOption.AllDirectories),
        ];

        List<string> missing = [];
        foreach (string source in sources.Where(s => !s.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            foreach (Match match in FrameworkAssetUrl().Matches(File.ReadAllText(source)))
            {
                string relative = match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar);
                if (!File.Exists(DeliveredAsset(root, relative)))
                {
                    missing.Add($"{Path.GetRelativePath(root, source)} -> /_nativeweb/{match.Groups[1].Value}");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "These markup references name a framework asset that libs/nativeweb.web/wwwroot/_nativeweb/ "
                + $"does not contain: {string.Join("; ", missing)}");
    }

    [GeneratedRegex(@"/_nativeweb/([A-Za-z0-9_./-]+)")]
    private static partial Regex FrameworkAssetUrl();

    /// <summary>
    /// The markup scanned by
    /// <see cref="EveryFrameworkAssetUrlInConsumerMarkupIsDelivered"/> references
    /// framework assets at all, so a regex that silently stops matching cannot
    /// pass that test by finding nothing.
    /// </summary>
    [Fact]
    public void TheFrameworkAssetScanFindsTheReferencesItIsChecking()
    {
        string root = RepoRoot();
        string layout = File.ReadAllText(
            Path.Combine(root, "skeleton", "apps", "__product__", "templates", "layout", "page.html"));

        Assert.Equal(4, FrameworkAssetUrl().Count(layout));
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
            .Where(d => !d.Replace('\\', '/').Contains("/.worktrees/", StringComparison.Ordinal))
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
