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
        "docs/README.md",
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

    /// <summary>
    /// Key-shaped literals in the two binders that are not configuration keys.
    /// </summary>
    /// <remarks>
    /// The sweep that uses this cannot tell a key from any other PascalCase string, so
    /// each one that is not a key is named here rather than the sweep being loosened.
    /// A loosened sweep is how a key added through an unrecognised call shape goes
    /// unnoticed, which is the case the sweep exists for.
    /// </remarks>
    private static readonly HashSet<string> NotKeys = [];

    private static readonly Dictionary<string, (string Claim, string Why)[]> ExemptClaims = new()
    {
        ["libs/nativeweb.assets/README.md"] =
        [
            ("apps/web", "Past tense: names the application that served these assets before it left on 2026-08-31."),
        ],
        ["docs/README.md"] =
        [
            ("scripts/cleanup/cleanup-<milestone>.ps1",
             "A naming pattern quoted from conventions.md, in the footnote reporting that nothing in the tree is named that way. A path that resolved would falsify the sentence."),
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

    /// <summary>
    /// Every runtime library under <c>libs/</c> has a README.
    /// </summary>
    /// <remarks>
    /// The previous version of this test swept <c>libs/</c> for READMEs and asserted
    /// the sweep contained them, which is the same enumeration on both sides of the
    /// assertion and could not fail. The check that earns its place is the one the
    /// sweep cannot make: a library with no README at all is invisible to a sweep of
    /// READMEs. The three <c>.generator</c> projects are excluded because their
    /// documentation lives in the runtime library they generate for, which
    /// <c>docs/README.md</c> states.
    /// </remarks>
    [Fact]
    public void EveryRuntimeLibrary_HasAReadme()
    {
        string root = RepoRoot();

        foreach (string directory in Directory.EnumerateDirectories(Path.Combine(root, "libs")).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(directory);
            if (name.EndsWith(".generator", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(
                File.Exists(Path.Combine(directory, "README.md")),
                $"libs/{name} has no README.md. A library nothing describes is one a consumer hand-ports, "
                + "which is the cost this documentation exists to stop.");
        }
    }

    [Fact]
    public void EveryPresentTenseDoc_Exists()
    {
        string root = RepoRoot();

        foreach (string doc in PresentTenseDocs)
        {
            Assert.True(
                File.Exists(Path.Combine(root, doc)),
                $"{doc} is listed in {nameof(PresentTenseDocs)} and is not in the tree. It was moved or "
                + "removed; update the list, and do not leave the guard silently narrower than it reads.");
        }
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
        HashSet<string> corpus = SourceCorpus(root);

        foreach (string symbol in SymbolClaims(text))
        {
            // Whole words. A plain substring search passes on any identifier that contains
            // the name: `Take` is satisfied by TakeAsync, `Set` by SetApplicationName, `For`
            // by ForwardedHeaders. Those are the short members most likely to be renamed and
            // least likely to be noticed, so the loosest form of the check covered the case
            // it was least able to afford.
            Assert.True(
                corpus.Contains(symbol),
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
            // Backticked forms only. A bare substring search over the whole file passes on a
            // mention in prose, and worse, passes on any superstring: documenting
            // `ClientAuthenticationZ` would satisfy a search for ClientAuthentication and the
            // key would read as documented while nothing described it.
            Assert.True(
                reference.Contains($"`{key}`", StringComparison.Ordinal)
                    || reference.Contains($"`{{Section}}:{key}`", StringComparison.Ordinal),
                $"{ConfigurationReference} does not document `{key}`, which a binder reads. A setting that "
                + "lands undocumented is one an operator finds by reading source.");
        }
    }

    [Fact]
    public void TheConfigurationReference_NamesNoKeyTheBindersDoNotRead()
    {
        string root = RepoRoot();
        HashSet<string> bound = [.. BoundKeys(root)];

        string reference = File.ReadAllText(Path.Combine(root, ConfigurationReference));

        foreach (string documented in DocumentedKeys(reference).Concat(DocumentedSignInKeys(reference)))
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

    /// <summary>
    /// The key extractors still see every read in the two binders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every check above rests on <see cref="BoundKeys"/> being complete, and it is
    /// complete only for the call shapes its regexes spell out: <c>Key("...")</c>,
    /// <c>Text/Flag/Seconds/Mode(section, "...")</c>, <c>section["..."]</c>,
    /// <c>section.GetSection("...")</c>, <c>configuration["..."]</c>. A key read
    /// tomorrow through a new helper -- <c>Uri(section, "...")</c>, say, or
    /// <c>GetValue&lt;T&gt;</c> -- matches none of them, and the parity tests stay green
    /// over a reference that no longer names every setting. That is the failure this
    /// whole file exists to stop, one level down.
    /// </para>
    /// <para>
    /// No test can enumerate a shape nobody has written yet, so this bounds it instead:
    /// the counts are pinned, and a read added in an unseen shape leaves them unmoved
    /// while the binder grows. Update the numbers when a key is added, and if adding one
    /// did not move them, the extractor cannot see it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheKeyExtractors_StillSeeEveryKeyShapedLiteralInTheBinders()
    {
        string root = RepoRoot();
        HashSet<string> bound = [.. BoundKeys(root)];

        foreach (string file in (string[])
                 ["libs/nativeweb.web/NativeWebOptions.cs", "libs/nativeweb.signin/SignInConfiguration.cs"])
        {
            // Comments are stripped first. A `cref` is quoted the same way a key is, and the
            // prose quotes values an operator might write -- "1", "yes", "Enabled" -- so
            // leaving comments in fills the exemption set with names that were never
            // candidates, and an exemption set full of noise is one nobody reads.
            string source = string.Join(
                '\n',
                File.ReadAllLines(Path.Combine(root, file))
                    .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

            foreach (Match match in StringLiteral().Matches(source))
            {
                string literal = match.Groups[1].Value;

                if (!KeyShape().IsMatch(literal) || NotKeys.Contains(literal))
                {
                    continue;
                }

                Assert.True(
                    bound.Contains(literal) || bound.Contains(literal.Split(':')[^1]),
                    $"{file} contains the key-shaped literal \"{literal}\" and no extractor in "
                    + $"{nameof(BoundKeys)} returns it. Either it is a key read through a call shape the "
                    + $"extractors do not match -- add the shape -- or it is not a key, in which case add "
                    + $"it to {nameof(NotKeys)}. Left alone, a setting binds and the reference never "
                    + "names it, with every check in this file green.");
            }
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

        // Not every root-level read goes through Key(): the OpenTelemetry service name is read
        // off the configuration root by its standard name, so the section-relative patterns
        // above cannot see it.
        foreach (Match match in RootKey().Matches(options))
        {
            yield return match.Groups[1].Value;
        }

        string signIn = File.ReadAllText(Path.Combine(root, "libs/nativeweb.signin/SignInConfiguration.cs"));

        foreach (Match match in SignInKey().Matches(signIn))
        {
            yield return match.Groups[2].Value;
        }

        // The helper calls above are the common shape, not the only one. Three keys are read
        // straight off the section -- an indexer and a GetSection -- and a fourth is passed to
        // Mode() as a variable rather than a literal, so the helper pattern matches nothing for
        // it. Extracting only the tidy shape is how a key gets added to a settings class and
        // never to the reference, with this test green throughout.
        foreach (Match match in SectionKey().Matches(signIn))
        {
            yield return match.Groups[1].Value;
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

    /// <summary>
    /// The keys the sign-in table documents, taken from the first cell of each of its rows.
    /// </summary>
    /// <remarks>
    /// The framework half of the reference writes keys section-relative, so
    /// <see cref="SectionRelativeKey"/> finds them. The sign-in half cannot: the section
    /// is named by whatever the product passes to <c>AddSignIn</c>, so its keys are
    /// written bare. Without this the reverse check saw none of them and every sign-in
    /// row could outlive the key it describes -- the configuration.md failure mode, over
    /// twenty keys, with the suite green.
    /// </remarks>
    private static IEnumerable<string> DocumentedSignInKeys(string reference)
    {
        int start = reference.IndexOf("## Sign-in settings", StringComparison.Ordinal);
        Assert.True(
            start >= 0,
            $"{ConfigurationReference} has no `## Sign-in settings` heading, so the sign-in keys it "
            + "documents are read by nothing here. Rename the heading in this test with the document.");

        int end = reference.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        string section = end < 0 ? reference[start..] : reference[start..end];
        HashSet<string> seen = [];

        foreach (string line in section.Split('\n'))
        {
            if (!line.StartsWith("| `", StringComparison.Ordinal))
            {
                continue;
            }

            string cell = line[1..line.IndexOf('|', 1)];

            foreach (Match match in BacktickedSpan().Matches(cell))
            {
                string key = match.Groups[1].Value.Trim();
                if (SignInKeyShape().IsMatch(key) && seen.Add(key))
                {
                    yield return key;
                }
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

        // Unfiltered. Guarding a document behind File.Exists means renaming it removes it
        // from every check in this class and nothing goes red -- the guard erases its own
        // scope on the exact event it exists to catch. EveryPresentTenseDoc_Exists gives
        // that case a message; here it is a missing-file failure, which is also correct.
        foreach (string doc in PresentTenseDocs)
        {
            yield return doc;
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

        // A fenced block is invisible to the single-backtick sweep above, and the adoption
        // guide's most consequential path is inside one: the Import of
        // NativeWeb.Web.targets, without which every framework asset 404s at run time on an
        // adopter's site. Only $(NativeWebRoot)-rooted paths are taken -- the other paths in
        // those blocks are relative to the reader's own product, not to this tree, and
        // resolving them here would assert something false.
        foreach (Match match in FrameworkRootedPath().Matches(text))
        {
            string claim = match.Groups[1].Value.Replace('\\', '/');

            if (seen.Add(claim))
            {
                yield return claim;
            }
        }
    }

    private static HashSet<string>? _corpus;

    /// <summary>
    /// Every identifier in the sources, as whole words.
    /// </summary>
    /// <remarks>
    /// A set of identifiers rather than one concatenated string, because a substring
    /// search over the string form answers a question nobody asked: <c>Take</c> is
    /// satisfied by <c>TakeAsync</c>, <c>Set</c> by <c>SetApplicationName</c>,
    /// <c>For</c> by <c>ForwardedHeaders</c>. The short members are the ones a rename
    /// is most likely to move and least likely to be noticed moving, so the loosest
    /// form of this check covered the case it could least afford to.
    /// </remarks>
    private static HashSet<string> SourceCorpus(string root)
    {
        if (_corpus is not null)
        {
            return _corpus;
        }

        StringBuilder builder = new();

        // tests/ is in the corpus because a README names the suite that proves it, down to
        // the test method. Excluding it made libs/nativeweb.scaffold/README.md, which cites
        // a test by name, report a symbol that exists.
        foreach (string area in (string[])["libs", "apps", "tests", "skeleton"])
        {
            // Build files as well as sources: an adoption guide names MSBuild properties --
            // PackageId, NativeWebRoot, ReferenceOutputAssembly -- and those are as real as a
            // method name and as capable of being wrong.
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, area), "*.*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".cs", StringComparison.Ordinal)
                    && !file.EndsWith(".csproj", StringComparison.Ordinal)
                    && !file.EndsWith(".props", StringComparison.Ordinal)
                    && !file.EndsWith(".targets", StringComparison.Ordinal))
                {
                    continue;
                }

                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                builder.Append(File.ReadAllText(file));
                builder.Append('\n');
            }
        }

        HashSet<string> identifiers = [];

        foreach (Match match in Identifier().Matches(builder.ToString()))
        {
            identifiers.Add(match.Value);
        }

        return _corpus = identifiers;
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

            // A diagnostic id, not a symbol. Ours (NWT001, NWF010) appear in the generator
            // sources and pass on their own; the ones documented here that do not are
            // Microsoft's -- CS0246 from Roslyn, MSB9008 from MSBuild.
            if (DiagnosticId().IsMatch(claim))
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

    [GeneratedRegex(@"^[A-Z]{2,5}[0-9]{3,5}$")]
    private static partial Regex DiagnosticId();

    [GeneratedRegex(@"Key\(""([^""]+)""\)")]
    private static partial Regex FrameworkKey();

    [GeneratedRegex(@"Key\((\w+)\.(\w+)\)")]
    private static partial Regex IndirectFrameworkKey();

    private static Regex ConstantDeclaration(string name) =>
        new($@"const\s+string\s+{Regex.Escape(name)}\s*=\s*""([^""]+)""");

    [GeneratedRegex(@"(Text|Flag|Seconds|Mode)\(\s*section,\s*""([^""]+)""")]
    private static partial Regex SignInKey();

    [GeneratedRegex(@"section(?:\[|\.GetSection\()\s*""([^""]+)""")]
    private static partial Regex SectionKey();

    [GeneratedRegex(@"configuration\[\s*""([^"":]+)""\s*\]")]
    private static partial Regex RootKey();

    [GeneratedRegex(@"`\{Section\}:([^`]+)`")]
    private static partial Regex SectionRelativeKey();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex SignInKeyShape();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"""([^""\\\n]*)""")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]{2,}(:[A-Z][A-Za-z0-9]{2,})*$")]
    private static partial Regex KeyShape();

    [GeneratedRegex(@"\$\(NativeWebRoot\)[\\/]((?:libs|apps|docs|db|scripts|tools|tests|skeleton|work)[\w.\\/-]+)")]
    private static partial Regex FrameworkRootedPath();

    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex MarkdownLink();
}
