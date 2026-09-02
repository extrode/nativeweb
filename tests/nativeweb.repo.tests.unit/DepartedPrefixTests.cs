using System.Text.RegularExpressions;
using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// No library names a <c>NativeWeb:</c>-prefixed configuration key.
/// </summary>
/// <remarks>
/// <para>
/// That prefix belonged to <c>apps/web</c>, which left on 2026-08-31. No library in
/// <c>libs/</c> binds configuration at all — the host owns the section and chooses the
/// prefix — so a key written under that name is one a reader can set and nothing will
/// read. Two survived the departure: <c>SmtpOptions.Validate</c> threw
/// <c>NativeWeb:Smtp:FromAddress</c> at an operator whose configuration says
/// <c>Sample:Smtp:FromAddress</c>, and <c>ContactProtector</c> named
/// <c>NativeWeb:ContactKey</c> in four comments.
/// </para>
/// <para>
/// The comments are the reason this is a test rather than one pass of find-and-replace.
/// A draft README documented both as settings before the prefix was noticed, because a
/// comment is what documentation gets written from; the sweep is what stops the next
/// copy.
/// </para>
/// </remarks>
public sealed partial class DepartedPrefixTests
{
    [Fact]
    public void NoLibrarySource_NamesADepartedConfigurationKey()
    {
        string libs = Path.Combine(RepoRoot(), "libs");

        foreach (string file in Directory
                     .EnumerateFiles(libs, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".cs", StringComparison.Ordinal)
                                    || path.EndsWith(".md", StringComparison.Ordinal))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            Match match = DepartedKey().Match(File.ReadAllText(file));

            Assert.False(
                match.Success,
                $"{Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/')} names \"{match.Value}\". "
                + "No library here binds configuration, and the NativeWeb: prefix left with apps/web on "
                + "2026-08-31, so that key is one an operator can set and nothing will read. Write the "
                + "key section-relative, or name no key at all.");
        }
    }

    // Requires an uppercase letter after the colon so the key shape is what matches and
    // ordinary prose does not: every README opens "... for NativeWeb: a source-generated
    // ...", and a sentence is not a setting.
    [GeneratedRegex(@"NativeWeb:[A-Z][A-Za-z0-9]*")]
    private static partial Regex DepartedKey();

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
