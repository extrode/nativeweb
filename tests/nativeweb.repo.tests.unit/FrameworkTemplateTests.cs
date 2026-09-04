using System.Xml.Linq;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The framework's own templates live in one place, and a consumer's copy of one
/// is either declared or absent (spec 029 R8).
/// </summary>
/// <remarks>
/// <para>
/// <c>NativeWeb.Web.targets</c> already refuses an undeclared clash at build
/// time, and that is the guard that protects a consumer outside this repository.
/// This one exists because the build-time error is only reached by a project
/// somebody builds: a copy reintroduced under <c>skeleton/</c> would be caught
/// when a stamped product builds, which is a CI job and a stamp away, and a copy
/// under a consumer nobody has built yet is caught by nothing at all.
/// </para>
/// <para>
/// The failure this stops is not the clash — MSBuild handles that. It is the
/// quiet re-vendoring that comes after a framework template is edited and
/// somebody "fixes" the product by pasting the old version back beside it,
/// which is how the CSS copies this repo is still carrying came to exist.
/// </para>
/// </remarks>
public sealed class FrameworkTemplateTests
{
    private const string FrameworkTemplates = "libs/nativeweb.web/templates";

    /// <summary>
    /// Every project that compiles templates of its own, and the csproj that
    /// would declare an override.
    /// </summary>
    private static readonly (string Templates, string Project)[] Consumers =
    [
        ("apps/sample/templates", "apps/sample/nativeweb.sample.csproj"),
        ("skeleton/apps/__product__/templates", "skeleton/apps/__product__/__product__.csproj"),
    ];

    [Fact]
    public void TheFrameworkOwnsAtLeastOneTemplate()
    {
        Assert.NotEmpty(LogicalPaths(Path.Combine(RepoRoot(), FrameworkTemplates)));
    }

    [Theory]
    [MemberData(nameof(ConsumerData))]
    public void AConsumerCopyOfAFrameworkTemplateIsDeclaredOrAbsent(string templates, string project)
    {
        string root = RepoRoot();

        string[] framework = LogicalPaths(Path.Combine(root, FrameworkTemplates));
        string[] mine = LogicalPaths(Path.Combine(root, templates));
        string[] declared = Overrides(Path.Combine(root, project));

        string[] undeclared = [.. framework
            .Intersect(mine, StringComparer.Ordinal)
            .Except(declared, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.True(
            undeclared.Length == 0,
            $"{templates} carries framework-owned template(s) {string.Join(", ", undeclared)} with "
                + $"no matching <NativeWebTemplateOverride> in {project}. Delete the copy to take "
                + "the framework's, or declare the override to keep your own. Leaving both is a "
                + "silent last-wins in the render generator, which keys its model dictionary on "
                + "the logical path.");
    }

    /// <summary>
    /// An override naming a template the framework does not ship is dead
    /// configuration, and it reads as protection that is not there.
    /// </summary>
    [Theory]
    [MemberData(nameof(ConsumerData))]
    public void EveryDeclaredOverrideNamesATemplateTheFrameworkShips(string templates, string project)
    {
        string root = RepoRoot();

        string[] framework = LogicalPaths(Path.Combine(root, FrameworkTemplates));
        string[] stale = [.. Overrides(Path.Combine(root, project))
            .Except(framework, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.True(
            stale.Length == 0,
            $"{project} declares <NativeWebTemplateOverride> for {string.Join(", ", stale)}, which "
                + $"{FrameworkTemplates} does not contain. The declaration suppresses nothing and "
                + $"the file at {templates} is now the only copy. Delete the declaration.");
    }

    /// <summary>
    /// A consumer added to the tree but not to <see cref="Consumers"/> would be
    /// unguarded and silent, so the list is checked against the tree.
    /// </summary>
    /// <remarks>
    /// Keyed on importing <c>NativeWeb.Web.targets</c> rather than on having a
    /// <c>templates/</c> directory. A project that does not import it never
    /// receives a framework template and so cannot clash with one:
    /// <c>tests/nativeweb.render.tests.unit/templates</c> holds generator
    /// fixtures, references <c>nativeweb.render</c> alone, and is not a consumer
    /// however much its directory name looks like one.
    /// </remarks>
    [Fact]
    public void EveryProjectThatReceivesFrameworkTemplatesIsDeclaredHere()
    {
        string root = RepoRoot();

        string[] importers = [.. Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !Excluded(Path.GetRelativePath(root, p).Replace('\\', '/')))
            .Where(ImportsTheWebTargets)
            .Select(p => Path.GetRelativePath(root, Path.GetDirectoryName(p)!).Replace('\\', '/'))
            .Select(d => $"{d}/templates")];

        Assert.True(
            importers.Length > 0,
            "No project in the tree imports NativeWeb.Web.targets, so this test asserts nothing. "
                + "The delivery mechanism has moved and the override check no longer covers "
                + "anything.");

        string[] undeclared = [.. importers
            .Except(Consumers.Select(c => c.Templates), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.True(
            undeclared.Length == 0,
            "These projects receive the framework's templates and are not covered by the override "
                + $"check. Add each to FrameworkTemplateTests.Consumers: {string.Join(", ", undeclared)}");
    }

    private static bool ImportsTheWebTargets(string project) =>
        XDocument.Load(project)
            .Descendants()
            .Where(e => e.Name.LocalName == "Import")
            .Select(e => (string?)e.Attribute("Project"))
            .Any(p => p is not null
                && p.Replace('\\', '/').EndsWith("/NativeWeb.Web.targets", StringComparison.OrdinalIgnoreCase));

    public static TheoryData<string, string> ConsumerData()
    {
        TheoryData<string, string> data = [];
        foreach ((string templates, string project) in Consumers)
        {
            data.Add(templates, project);
        }

        return data;
    }

    /// <summary>
    /// Template paths relative to <paramref name="directory"/>, forward-slashed,
    /// which is the form <c>NativeWebTemplateOverride</c> and the generator's own
    /// logical path both reduce to.
    /// </summary>
    private static string[] LogicalPaths(string directory) =>
        Directory.Exists(directory)
            ? [.. Directory
                .EnumerateFiles(directory, "*.html", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(directory, f).Replace('\\', '/'))]
            : [];

    private static string[] Overrides(string project) =>
        [.. XDocument.Load(project)
            .Descendants()
            .Where(e => e.Name.LocalName == "NativeWebTemplateOverride")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(i => i is not null)
            .Select(i => i!.Replace('\\', '/'))];

    /// <summary>
    /// Build residue, scratch and worktrees. A worktree is a full checkout, so
    /// without this the same file is found once per worktree and reported as a
    /// consumer nobody added.
    /// </summary>
    private static bool Excluded(string relative) =>
        relative.StartsWith("tmp/", StringComparison.Ordinal)
        || relative.StartsWith(".worktrees/", StringComparison.Ordinal)
        || relative.StartsWith(".claude/", StringComparison.Ordinal)
        || relative.Contains("/bin/", StringComparison.Ordinal)
        || relative.Contains("/obj/", StringComparison.Ordinal);

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
