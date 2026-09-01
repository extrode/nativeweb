using System.Text;
using System.Text.RegularExpressions;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// Documentation that describes the present names things that exist.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/02-reference/configuration.md</c> is why this exists. It was 302 lines
/// describing <c>apps/web</c> five months after that application left the
/// repository, served the whole time at <c>/docs</c> on the deployed site, and
/// nothing noticed. It had been guarded once --
/// <c>tests/nativeweb.web.tests.unit/ConfigurationReferenceTests.cs</c> failed when
/// the file drifted from the source -- and that test was deleted along with the
/// application it read, taking the guarantee with it. This is the replacement,
/// placed in the repository tier so it cannot leave with any one application.
/// </para>
/// <para>
/// Only documents in the present tense qualify. A removal log is required to name
/// things that no longer exist -- <c>breaking-changes.md</c> exists to record that
/// <c>libs/nativeweb.auth</c> is gone, and a version of it whose every path
/// resolved would be a broken removal log. Those documents are absent from
/// <see cref="PresentTenseDocs"/> rather than exempted inside it, because the
/// distinction is about what the document is for.
/// </para>
/// <para>
/// Library READMEs are swept rather than listed, so a new library is covered the
/// day it gets one. <see cref="ExemptClaims"/> is the escape hatch for a
/// past-tense sentence inside an otherwise present-tense document, and every entry
/// states why. It is keyed by document so an exemption cannot silently widen to
/// another file.
/// </para>
/// </remarks>
public sealed partial class DocsClaimsTests
{
    private static readonly string[] PresentTenseDocs =
    [
        "docs/02-reference/configuration.md",
        "docs/00-quick-start/adopting-in-an-existing-app.md",
    ];

    private static readonly string[] RepoRoots =
    [
        "libs/", "apps/", "docs/", "db/", "scripts/", "tools/", "tests/", "skeleton/", "work/",
    ];

    private const string ConfigurationReference = "docs/02-reference/configuration.md";

    private static readonly string[] DocumentedAsUnread =
    [
        "ForwardedHeaders:ForwardLimit",
    ];

    private static readonly Dictionary<string, (string Claim, string Why)[]> ExemptClaims = new()
    {
        ["libs/nativeweb.assets/README.md"] =
        [
            ("apps/web", "Past tense: names the application that served these assets before it left on 2026-08-31."),
        ],
    };

    public static TheoryData<string> Documents()
    {
        var data = new TheoryData<string>();
        foreach (string doc in EnumerateDocuments())
        {
            data.Add(doc);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryRepositoryPathNamed_Exists(string relativePath)
    {
        string root = RepoRoot();
        string text = File.ReadAllText(Path.Combine(root, relativePath));
        (string Claim, string Why)[] exempt =
            ExemptClaims.TryGetValue(relativePath, out (string Claim, string Why)[]? found) ? found : [];

        foreach (string claim in PathClaims(text))
        {
            if (Array.Exists(exempt, x => x.Claim == claim))
            {
                continue;
            }

            string full = Path.Combine(root, claim.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(
                File.Exists(full) || Directory.Exists(full),
                $"{relativePath} names `{claim}`, which is not in the tree. Either the path moved and the "
                + "document was not updated, or the sentence is about something that no longer exists -- in "
                + $"which case add it to {nameof(ExemptClaims)} with the reason.");
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryRelativeLink_Resolves(string relativePath)
    {
        string root = RepoRoot();
        string full = Path.Combine(root, relativePath);
        string directory = Path.GetDirectoryName(full)!;

        foreach (string target in RelativeLinks(File.ReadAllText(full)))
        {
            string resolved = Path.GetFullPath(Path.Combine(directory, target.Replace('/', Path.DirectorySeparatorChar)));
            Assert.True(
                File.Exists(resolved) || Directory.Exists(resolved),
                $"{relativePath} links to `{target}`, which resolves to nothing.");
        }
    }

    [Fact]
    public void EveryLibraryReadme_IsCovered()
    {
        string root = RepoRoot();
        List<string> swept = [];

        foreach (string directory in Directory.EnumerateDirectories(Path.Combine(root, "libs")))
        {
            string readme = Path.Combine(directory, "README.md");
            if (File.Exists(readme))
            {
                swept.Add(Path.GetRelativePath(root, readme).Replace('\\', '/'));
            }
        }

        List<string> covered = [.. EnumerateDocuments()];
        Assert.All(swept, readme => Assert.Contains(readme, covered));
    }

    [Fact]
    public void EveryExemption_NamesADocumentThatIsChecked()
    {
        List<string> covered = [.. EnumerateDocuments()];
        Assert.All(ExemptClaims.Keys, document => Assert.Contains(document, covered));
    }

    [Fact]
    public void EveryExemption_IsStillNeeded()
    {
        string root = RepoRoot();

        foreach ((string document, (string Claim, string Why)[] exemptions) in ExemptClaims)
        {
            string text = File.ReadAllText(Path.Combine(root, document));
            List<string> claims = [.. PathClaims(text)];

            foreach ((string claim, _) in exemptions)
            {
                Assert.True(
                    claims.Contains(claim),
                    $"{document} no longer names `{claim}`, so its exemption is dead. Remove it from "
                    + $"{nameof(ExemptClaims)}.");
            }
        }
    }

    /// <summary>
    /// A backticked type or member name in a document is a name that appears in the
    /// C# sources.
    /// </summary>
    /// <remarks>
    /// Catches the failure the path check cannot: a README naming a method that was
    /// renamed, or one that never existed. The corpus is every <c>.cs</c> file under
    /// <c>libs/</c> and <c>apps/</c> rather than the one library, because a README
    /// legitimately names framework types -- <c>HttpContext</c>, <c>WebApplication</c>
    /// -- and those appear in our own signatures. That makes this a check against
    /// invention, not against every kind of drift, which is the honest description of
    /// what a name-existence test can do.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Documents))]
    public void EverySymbolNamed_AppearsInTheSources(string relativePath)
    {
        string root = RepoRoot();
        string text = File.ReadAllText(Path.Combine(root, relativePath));
        string corpus = SourceCorpus(root);

        foreach (string symbol in SymbolClaims(text))
        {
            Assert.True(
                corpus.Contains(symbol, StringComparison.Ordinal),
                $"{relativePath} names `{symbol}`, which appears nowhere in libs/ or apps/. Either it was "
                + "renamed and the document was not updated, or it does not exist.");
        }
    }

    /// <summary>
    /// Keys the reference documents that no binder builds, and keys a binder builds
    /// that the reference does not document.
    /// </summary>
    /// <remarks>
    /// The expectation is extracted from the two binders at run time rather than
    /// held as a list here, because a list here would go stale in the same way the
    /// document does. <c>ForwardedHeaders:ForwardLimit</c> is the one key the
    /// reference names that no binder reads, and it says so in a section about not
    /// relying on it -- <see cref="DocumentedAsUnread"/> is how that stays sayable
    /// without weakening the check.
    /// </remarks>
    [Fact]
    public void TheConfigurationReference_NamesEveryKeyTheBindersRead()
    {
        string root = RepoRoot();
        string reference = File.ReadAllText(Path.Combine(root, ConfigurationReference));

        foreach (string key in BoundKeys(root))
        {
            Assert.True(
                reference.Contains($"`{key}`", StringComparison.Ordinal)
                    || reference.Contains($"`{{Section}}:{key}`", StringComparison.Ordinal)
                    || reference.Contains($"`{key}`,", StringComparison.Ordinal)
                    || reference.Contains(key, StringComparison.Ordinal),
                $"{ConfigurationReference} does not document `{key}`, which a binder reads. A setting that "
                + "lands undocumented is one an operator finds by reading source.");
        }
    }

    [Fact]
    public void TheConfigurationReference_NamesNoKeyTheBindersDoNotRead()
    {
        string root = RepoRoot();
        HashSet<string> bound = [.. BoundKeys(root)];

        foreach (string documented in DocumentedKeys(File.ReadAllText(Path.Combine(root, ConfigurationReference))))
        {
            if (DocumentedAsUnread.Contains(documented))
            {
                continue;
            }

            Assert.True(
                bound.Contains(documented),
                $"{ConfigurationReference} documents `{{Section}}:{documented}`, which no binder reads. Either "
                + "the key was removed from the source and the document was not updated, or it never existed.");
        }
    }

    [Fact]
    public void EveryKeyDocumentedAsUnread_IsStillUnread()
    {
        string root = RepoRoot();
        HashSet<string> bound = [.. BoundKeys(root)];

        foreach (string key in DocumentedAsUnread)
        {
            Assert.False(
                bound.Contains(key),
                $"`{key}` is now bound, so the section of {ConfigurationReference} saying it is read by nothing "
                + "is wrong. Move it into the settings table and drop it from "
                + $"{nameof(DocumentedAsUnread)}.");
        }
    }

    private static IEnumerable<string> BoundKeys(string root)
    {
        string options = File.ReadAllText(Path.Combine(root, "libs/nativeweb.web/NativeWebOptions.cs"));

        foreach (Match match in FrameworkKey().Matches(options))
        {
            yield return match.Groups[1].Value;
        }

        // Key(SecurityTxtEndpoint.ConfigurationKey) rather than Key("SecurityContact"). A
        // literal-only extractor silently loses these, which reads as "the document has an
        // extra key" rather than "the reader cannot see one".
        foreach (Match match in IndirectFrameworkKey().Matches(options))
        {
            string constant = match.Groups[2].Value;
            string? resolved = null;

            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "libs/nativeweb.web"), "*.cs"))
            {
                Match declaration = ConstantDeclaration(constant).Match(File.ReadAllText(file));
                if (declaration.Success)
                {
                    resolved = declaration.Groups[1].Value;
                    break;
                }
            }

            Assert.True(
                resolved is not null,
                $"NativeWebOptions builds a key from {match.Groups[1].Value}.{constant}, and no const string "
                + "of that name is declared in libs/nativeweb.web. The extractor cannot resolve it, so the "
                + "key would go unchecked.");

            yield return resolved!;
        }

        string signIn = File.ReadAllText(Path.Combine(root, "libs/nativeweb.signin/SignInConfiguration.cs"));

        foreach (Match match in SignInKey().Matches(signIn))
        {
            yield return match.Groups[2].Value;
        }
    }

    private static IEnumerable<string> DocumentedKeys(string reference)
    {
        HashSet<string> seen = [];

        foreach (Match match in SectionRelativeKey().Matches(reference))
        {
            if (seen.Add(match.Groups[1].Value))
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    private static IEnumerable<string> EnumerateDocuments()
    {
        string root = RepoRoot();

        foreach (string directory in Directory.EnumerateDirectories(Path.Combine(root, "libs")).Order(StringComparer.Ordinal))
        {
            string readme = Path.Combine(directory, "README.md");
            if (File.Exists(readme))
            {
                yield return Path.GetRelativePath(root, readme).Replace('\\', '/');
            }
        }

        foreach (string doc in PresentTenseDocs)
        {
            if (File.Exists(Path.Combine(root, doc)))
            {
                yield return doc;
            }
        }
    }

    private static IEnumerable<string> PathClaims(string text)
    {
        HashSet<string> seen = [];

        foreach (Match match in BacktickedSpan().Matches(text))
        {
            string claim = match.Groups[1].Value.Trim().TrimEnd('.', ',', ';', ':', ')');

            if (!claim.Contains('/', StringComparison.Ordinal)
                || claim.Contains('*', StringComparison.Ordinal)
                || claim.Contains(' ', StringComparison.Ordinal)
                || !Array.Exists(RepoRoots, r => claim.StartsWith(r, StringComparison.Ordinal)))
            {
                continue;
            }

            if (seen.Add(claim))
            {
                yield return claim;
            }
        }
    }

    private static string? _corpus;

    private static string SourceCorpus(string root)
    {
        if (_corpus is not null)
        {
            return _corpus;
        }

        StringBuilder builder = new();

        // tests/ is in the corpus because a README names the suite that proves it, down to
        // the test method. Excluding it made EveryLibraryReadme_IsCovered's own subject --
        // libs/nativeweb.scaffold/README.md, which cites a test by name -- report a symbol
        // that exists.
        foreach (string area in (string[])["libs", "apps", "tests"])
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, area), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                builder.Append(File.ReadAllText(file));
            }
        }

        return _corpus = builder.ToString();
    }

    private static IEnumerable<string> SymbolClaims(string text)
    {
        HashSet<string> seen = [];

        foreach (Match match in BacktickedSpan().Matches(text))
        {
            string claim = match.Groups[1].Value.Trim();

            // Sample__Db__MaxAutoPrepare is a configuration key in its environment-variable
            // spelling, not an identifier. It matches the shape below and is checked by the
            // key-parity tests instead.
            if (claim.Contains("__", StringComparison.Ordinal))
            {
                continue;
            }

            // SCREAMING_SNAKE is an environment variable, not an identifier, and the ones
            // worth documenting include OTEL_EXPORTER_OTLP_ENDPOINT -- read by the
            // OpenTelemetry SDK and named nowhere in this repository's sources.
            if (claim.Contains('_', StringComparison.Ordinal)
                && !claim.Any(char.IsLower))
            {
                continue;
            }

            // The identifier only: `Chrome.For(context, assets, ...)` is a claim about
            // Chrome and For, and the argument names are the reader's to choose.
            Match identifier = SymbolShape().Match(claim);
            if (!identifier.Success || identifier.Value.Length != claim.Length && !claim.StartsWith(identifier.Value + "(", StringComparison.Ordinal)
                && !claim.StartsWith(identifier.Value + "<", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string part in identifier.Value.Split('.'))
            {
                if (part.Length > 2 && char.IsUpper(part[0]) && seen.Add(part))
                {
                    yield return part;
                }
            }
        }
    }

    private static IEnumerable<string> RelativeLinks(string text)
    {
        HashSet<string> seen = [];

        foreach (Match match in MarkdownLink().Matches(text))
        {
            string target = match.Groups[1].Value.Split('#')[0].Trim();

            if (target.Length == 0
                || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seen.Add(target))
            {
                yield return target;
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

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex BacktickedSpan();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*")]
    private static partial Regex SymbolShape();

    [GeneratedRegex(@"Key\(""([^""]+)""\)")]
    private static partial Regex FrameworkKey();

    [GeneratedRegex(@"Key\((\w+)\.(\w+)\)")]
    private static partial Regex IndirectFrameworkKey();

    private static Regex ConstantDeclaration(string name) =>
        new($@"const\s+string\s+{Regex.Escape(name)}\s*=\s*""([^""]+)""");

    [GeneratedRegex(@"(Text|Flag|Seconds|Mode)\(\s*section,\s*""([^""]+)""")]
    private static partial Regex SignInKey();

    [GeneratedRegex(@"`\{Section\}:([^`]+)`")]
    private static partial Regex SectionRelativeKey();

    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex MarkdownLink();
}
