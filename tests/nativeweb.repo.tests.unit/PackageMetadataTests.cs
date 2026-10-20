using System.Xml.Linq;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The package metadata that names the publisher: set once, centrally, and not
/// through the retired org.
/// </summary>
/// <remarks>
/// <para>
/// Ten packable csproj files carried the legacy org's name in <c>&lt;Authors&gt;</c> for
/// three days after the 2026-08-26 rename to Extrode LLC, each of them one line
/// away from a <c>Copyright</c> that already said Extrode LLC, because that one
/// was central in <c>Directory.Build.targets</c> and Authors was not.
/// <c>OrgReferenceTests</c> does not catch it and should not: by design it bans
/// the old org only in things that RESOLVE -- a checkout <c>repository:</c>, a
/// feed host -- because those fail on a runner. Package metadata resolves
/// nothing, so it fails nowhere and is read by whoever installs the package.
/// </para>
/// <para>
/// The assertion is deliberately shaped as "one central value, no local ones"
/// rather than "no csproj names the old org". <c>Directory.Build.targets</c> is
/// imported after every csproj, so a per-project <c>&lt;Authors&gt;</c> is
/// overridden rather than merged: it has no effect on the produced .nupkg while
/// reading, in the file, as though it does. Banning the local form is what stops
/// the next rename from being ten edits again.
/// </para>
/// </remarks>
public sealed class PackageMetadataTests
{
    private const string Publisher = "Extrode LLC";

    [Fact]
    public void PackableProjectsTakeTheirAuthorsFromTheCentralTargets()
    {
        var targets = XDocument.Load(Path.Combine(RepoRoot(), "Directory.Build.targets"));

        // Namespace-agnostic for the reason LicenseFilesTests gives: this file
        // carries no xmlns today, and adding one later should not turn the
        // assertion into a no-op that passes on an empty sequence.
        string[] authors = [.. targets.Descendants()
            .Where(e => e.Name.LocalName == "Authors")
            .Select(e => e.Value.Trim())];

        Assert.True(
            authors.Length == 1,
            $"Expected exactly one central <Authors> in Directory.Build.targets, found {authors.Length}. "
                + "Without it every packable project falls back to its assembly name as the author, "
                + "which is what a consumer sees on the feed listing.");

        Assert.Equal(Publisher, authors[0]);
    }

    [Fact]
    public void NoPackableProjectDeclaresItsOwnAuthors()
    {
        string root = RepoRoot();

        string[] offenders = [.. PackableProjects(root)
            .Where(p => XDocument.Load(p)
                .Descendants()
                .Any(e => e.Name.LocalName == "Authors"))
            .Select(p => Path.GetRelativePath(root, p))];

        Assert.True(
            offenders.Length == 0,
            $"These declare their own <Authors>, which Directory.Build.targets overrides because it "
                + $"is imported after the csproj: {string.Join(", ", offenders)}. The value has no "
                + "effect on the .nupkg and will be read as though it does — which is how ten of "
                + "them went on naming the old org after the rename. Delete the line; the central "
                + "one applies.");
    }

    /// <summary>
    /// Every packable project under <c>libs/</c>, found by reading IsPackable.
    /// </summary>
    /// <remarks>
    /// Enumerated under <c>libs/</c> only, not from the repo root. A recursive
    /// walk from the root is the defect already logged against SdkPinTests and
    /// named in OrgReferenceTests: it descends into gitignored scratch, and
    /// <c>tmp/shallow-probe/</c> currently holds a full copy of these ten csproj
    /// files with the pre-rename Authors line still in them. Scanning that copy
    /// would fail this suite over a file nobody committed.
    /// </remarks>
    private static string[] PackableProjects(string root)
    {
        string libs = Path.Combine(root, "libs");

        Assert.True(Directory.Exists(libs), $"Expected packable projects under {libs}.");

        string[] projects = [.. Directory.GetFiles(libs, "*.csproj", SearchOption.AllDirectories)
            .Where(p => XDocument.Load(p)
                .Descendants()
                .Any(e => e.Name.LocalName == "IsPackable" && e.Value.Trim() == "true"))];

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
