using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// Nothing shipped names one identity provider (spec 016 R17).
/// </summary>
/// <remarks>
/// <para>
/// The library is a relying party and the provider is whatever the configuration names. That is
/// a claim about source, and source is where it decays: a hostname reaches a default, a name
/// reaches a comment, and the next person reads the comment as a supported-provider list. The
/// repository already carries the history — an identity provider lived here under framework
/// names until 2026-08-31, and <c>nativeweb.signin.epass</c> was a real project — so the way
/// this fails is by something coming back, not by something arriving new.
/// </para>
/// <para>
/// The skeleton is scanned alongside the library because it is stamped into every product: a
/// provider named in a template is a provider named in code nobody wrote.
/// <c>docs/</c> is not scanned, and neither are the tests — naming a provider while explaining
/// what the library is for, or while standing one up as a fixture, is what those files are for.
/// </para>
/// </remarks>
public sealed class ProviderNeutralityTests
{
    /// <summary>
    /// The provider's name and its hostname. The name subsumes the hostname as a substring; both
    /// are listed so a reader knows which two things this is about rather than inferring it.
    /// </summary>
    private static readonly string[] Provider = ["epass", "epass.io"];

    private static readonly string[] Scanned =
    [
        "libs/nativeweb.signin",
        "libs/nativeweb.signin.testing",
        "skeleton",
    ];

    [Fact]
    public void NothingShipped_NamesTheProvider()
    {
        string[] offenders = Offenders(ShippedSource()).ToArray();

        Assert.True(
            offenders.Length == 0,
            "The framework is a relying party against any conforming issuer, and these files name "
                + "one:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheScanner_FindsTheProviderWhenItIsThere()
    {
        // The control. Without it the fact above passes on a scanner that reads no files, matches
        // nothing, or was pointed at a directory that has since been renamed.
        (string Path, string Text)[] planted =
        [
            ("a.cs", "var issuer = \"https://epass.io/oauth\";"),
            ("b.cs", "// Works against EPass as well as anything else."),
            ("c.cs", "var issuer = configuration[\"AuthorizationEndpoint\"];"),
        ];

        Assert.Equal(["a.cs: epass", "b.cs: epass"], Offenders(planted).ToArray());
    }

    [Fact]
    public void TheScan_ReachesRealFiles()
    {
        // The other half of the control: a scanner that finds a planted needle is still useless
        // if it walks an empty tree. Every scanned root must contribute something.
        foreach (string root in Scanned)
        {
            Assert.True(
                ShippedSource().Any(file => file.Path.StartsWith(root, StringComparison.Ordinal)),
                $"{root} contributed no files to the scan.");
        }
    }

    private static IEnumerable<string> Offenders(IEnumerable<(string Path, string Text)> files) =>
        from file in files
        from term in Provider
        where file.Text.Contains(term, StringComparison.OrdinalIgnoreCase)
        group term by file.Path into byFile
        select $"{byFile.Key}: {byFile.First()}";

    private static IEnumerable<(string Path, string Text)> ShippedSource()
    {
        string repoRoot = RepoRoot();

        foreach (string root in Scanned)
        {
            string absolute = Path.Combine(repoRoot, root.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(absolute))
            {
                continue;
            }

            IEnumerable<string> files = Directory
                .EnumerateFiles(absolute, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path) is ".cs" or ".md" or ".csproj" or ".json" or ".props")
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

            foreach (string path in files)
            {
                yield return (
                    Path.GetRelativePath(repoRoot, path).Replace('\\', '/'),
                    File.ReadAllText(path));
            }
        }
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
