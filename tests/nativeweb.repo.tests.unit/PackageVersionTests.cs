using System.Xml.Linq;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// One version for the whole framework, and the skeleton pins that version
/// (spec 028 R1, R7).
/// </summary>
/// <remarks>
/// <para>
/// Thirteen packable projects each carried their own
/// <c>&lt;Version&gt;0.1.0&lt;/Version&gt;</c> until 2026-09-04. That is the
/// shape <c>PackageMetadataTests</c> already caught once with
/// <c>&lt;Authors&gt;</c>: ten copies of one string went on naming the retired
/// org for three days after the rename, because nothing made them move
/// together. A per-project version is worse than a per-project author, because
/// a consumer resolving <c>NativeWeb.Web 0.2.0</c> against a
/// <c>NativeWeb.Render</c> still at <c>0.1.0</c> gets a restore that succeeds
/// and a framework whose halves were never built together.
/// </para>
/// <para>
/// The skeleton assertion is the same invariant seen from the other side. A
/// stamped product pins five <c>NativeWeb.*</c> versions, and a pin naming a
/// version the framework does not publish fails as NU1101 inside somebody
/// else's repository, with nothing here to say why. That is the failure
/// a consumer pinned at <c>0.1.0</c> ran into, where its own comment said those
/// versions "resolve to nothing".
/// </para>
/// </remarks>
public sealed class PackageVersionTests
{
    [Fact]
    public void DirectoryBuildPropsDeclaresExactlyOneVersionPrefix()
    {
        string[] declared = VersionPrefixes(RepoRoot());

        Assert.True(
            declared.Length == 1,
            $"Expected exactly one <VersionPrefix> in Directory.Build.props, found {declared.Length}. "
                + "It is the source of truth for every NativeWeb.* package version, and tools/pack.ts "
                + "refuses to run when it is not.");
    }

    [Fact]
    public void NoPackableProjectDeclaresItsOwnVersion()
    {
        string root = RepoRoot();

        // All three override the central VersionPrefix, so banning only <Version>
        // leaves two spellings of the same mistake passing.
        string[] banned = ["Version", "VersionPrefix", "PackageVersion"];

        string[] offenders = [.. PackableProjects(root)
            .Select(p => (Path: Path.GetRelativePath(root, p), Doc: XDocument.Load(p)))
            .SelectMany(p => p.Doc
                .Descendants()
                .Where(e => banned.Contains(e.Name.LocalName))
                .Select(e => $"{p.Path} declares <{e.Name.LocalName}>"))];

        Assert.True(
            offenders.Length == 0,
            $"{string.Join(", ", offenders)}. A local one wins over the central VersionPrefix, "
                + "so the package it produces silently stops moving with the rest of the "
                + "framework. Delete the line.");
    }

    [Fact]
    public void SkeletonPinsMatchTheFrameworkVersion()
    {
        string root = RepoRoot();
        string version = VersionPrefixes(root).Single();

        (string? Id, string? Version)[] pins =
        [
            .. XDocument.Load(Path.Combine(root, "skeleton", "Directory.Packages.props"))
                .Descendants()
                .Where(e => e.Name.LocalName == "PackageVersion")
                .Select(e => ((string?)e.Attribute("Include"), (string?)e.Attribute("Version")))
                .Where(p => p.Item1 is not null && p.Item1.StartsWith("NativeWeb.", StringComparison.Ordinal)),
        ];

        Assert.True(
            pins.Length > 0,
            "skeleton/Directory.Packages.props pins no NativeWeb.* package. A stamped product "
                + "cannot build in package mode without them, and this test would assert nothing.");

        string[] wrong = [.. pins
            .Where(p => p.Version != version)
            .Select(p => $"{p.Id} pinned at {p.Version}")];

        Assert.True(
            wrong.Length == 0,
            $"The framework publishes {version} and the skeleton pins something else: "
                + $"{string.Join(", ", wrong)}. A stamped product would restore a version that was "
                + "never pushed, and the error it gets is NU1101 in its own repository.");
    }

    [Fact]
    public void EveryPackagePinnedInBothFilesCarriesOneVersion()
    {
        string root = RepoRoot();

        Dictionary<string, string> framework = Pins(Path.Combine(root, "Directory.Packages.props"));
        Dictionary<string, string> skeleton = Pins(Path.Combine(root, "skeleton", "Directory.Packages.props"));

        string[] shared = [.. framework.Keys
            .Where(skeleton.ContainsKey)
            .Where(id => !id.StartsWith("NativeWeb.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

        Assert.True(
            shared.Length > 0,
            "The two Directory.Packages.props files share no third-party pin. Either the skeleton "
                + "stopped pinning anything the framework pins, or this test stopped finding them; "
                + "both make it assert nothing.");

        string[] disagreeing = [.. shared
            .Where(id => framework[id] != skeleton[id])
            .Select(id => $"{id}: framework {framework[id]}, skeleton {skeleton[id]}")];

        Assert.True(
            disagreeing.Length == 0,
            $"{string.Join("; ", disagreeing)}. The skeleton's header says its pins are copied from "
                + "this repository, and a stamped product is where the disagreement surfaces - "
                + "against a version this repository never built or tested together.");
    }

    private static Dictionary<string, string> Pins(string propsFile) =>
        XDocument.Load(propsFile)
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageVersion")
            .Where(e => e.Attribute("Include") is not null && e.Attribute("Version") is not null)
            .ToDictionary(
                e => e.Attribute("Include")!.Value,
                e => e.Attribute("Version")!.Value,
                StringComparer.OrdinalIgnoreCase);

    private static string[] VersionPrefixes(string root) =>
        [.. XDocument.Load(Path.Combine(root, "Directory.Build.props"))
            .Descendants()
            .Where(e => e.Name.LocalName == "VersionPrefix")
            .Select(e => e.Value.Trim())];

    /// <summary>
    /// Every packable project under <c>libs/</c>, found by reading IsPackable.
    /// </summary>
    /// <remarks>
    /// Enumerated under <c>libs/</c> rather than from the repo root, for the
    /// reason <c>PackageMetadataTests</c> gives at length: a recursive walk from
    /// the root descends into gitignored scratch, and a copy of these csproj
    /// files under <c>tmp/</c> would fail this suite over a file nobody
    /// committed.
    /// </remarks>
    private static string[] PackableProjects(string root)
    {
        string libs = Path.Combine(root, "libs");

        Assert.True(Directory.Exists(libs), $"Expected packable projects under {libs}.");

        string[] projects = [.. Directory.GetFiles(libs, "*.csproj", SearchOption.AllDirectories)
            .Where(p => XDocument.Load(p)
                .Descendants()
                .Any(e => e.Name.LocalName == "IsPackable"
                    && e.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)))];

        Assert.True(
            projects.Length > 0,
            "Found no packable project under libs/. This test would then assert nothing at all, "
                + "so an IsPackable rename must fail here rather than pass silently.");

        return projects;
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
