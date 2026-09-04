using System.Reflection;
using System.Text.RegularExpressions;

using NativeWeb.Security;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

/// <summary>
/// The bot-trap markup and the constants that read it back cannot disagree
/// (spec 029 R7).
/// </summary>
/// <remarks>
/// <para>
/// <c>templates/shared/bot-traps.html</c> spells five field names as literal
/// text and <see cref="BotTraps"/> defines the same five as constants it matches
/// submitted form keys against. Nothing connects them: rename
/// <see cref="BotTraps.CanaryName"/> and the template still parses, the product
/// still compiles, every test still passes, and every submission from then on
/// arrives with a canary field the verdict code is no longer looking for. The
/// traps do not fire, so the failure looks exactly like a site with no bots.
/// </para>
/// <para>
/// Read off the type rather than listed here. A hand-copied list in this file
/// would be the third copy of the same five strings, and it would pass a rename
/// as readily as the template does.
/// </para>
/// <para>
/// The assertion is set equality in both directions, not containment. A template
/// carrying every constant plus an extra field is also wrong: an input the
/// verdict never reads is one a bot can fill with impunity, and it is how a
/// half-finished rename looks from here.
/// </para>
/// </remarks>
public sealed partial class BotTrapTemplateTests
{
    [Fact]
    public void TheTemplateNamesEveryTrapFieldAndNoOthers()
    {
        string[] declared = [.. TrapFieldNames().Order(StringComparer.Ordinal)];
        string[] rendered = [.. NameAttribute()
            .Matches(TemplateSource())
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(declared, rendered);
    }

    /// <summary>
    /// The two filled traps carry the value <see cref="BotTraps"/> expects to
    /// find unchanged, in the same input as their name.
    /// </summary>
    /// <remarks>
    /// A drifted value is the inverted failure of the test above: the traps fire
    /// on everyone instead of no one, because a human's browser submits the
    /// template's value and the verdict compares it against the constant.
    /// Matched per input element rather than per file, or a value sitting in
    /// some other field would satisfy it.
    /// </remarks>
    [Theory]
    [InlineData(BotTraps.HiddenFilledName, BotTraps.HiddenFilledValue)]
    [InlineData(BotTraps.OffscreenFilledName, BotTraps.OffscreenFilledValue)]
    public void AFilledTrapCarriesTheValueTheVerdictExpects(string field, string expected)
    {
        string[] values = [.. InputElement()
            .Matches(TemplateSource())
            .Select(m => m.Value)
            .Where(tag => NameAttribute().Match(tag) is { Success: true } name
                && name.Groups[1].Value == field)
            .Select(tag => ValueAttribute().Match(tag) is { Success: true } value
                ? value.Groups[1].Value
                : "")];

        Assert.Equal([expected], values);
    }

    [Fact]
    public void TheOffscreenWrapperUsesTheClassTheFrameworkDeclares()
    {
        Assert.Contains(
            BotTraps.OffscreenClass,
            ClassAttribute()
                .Matches(TemplateSource())
                .SelectMany(m => m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The value of every public string constant on <see cref="BotTraps"/> whose
    /// name ends in <c>Name</c>.
    /// </summary>
    private static string[] TrapFieldNames()
    {
        string[] found = [.. typeof(BotTraps)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Where(f => f.Name.EndsWith("Name", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)];

        Assert.True(
            found.Length > 1,
            "Found no trap-field constants on BotTraps, so this class asserts nothing. The naming "
                + "convention this reflects over (a public const string ending in 'Name') has "
                + "changed, and the template is now unguarded.");

        return found;
    }

    private static string TemplateSource() =>
        File.ReadAllText(Path.Combine(
            RepoRoot(),
            "libs",
            "nativeweb.web",
            "templates",
            "shared",
            "bot-traps.html"));

    [GeneratedRegex(@"\bname=""([^""]*)""")]
    private static partial Regex NameAttribute();

    [GeneratedRegex(@"\bvalue=""([^""]*)""")]
    private static partial Regex ValueAttribute();

    [GeneratedRegex(@"\bclass=""([^""]*)""")]
    private static partial Regex ClassAttribute();

    [GeneratedRegex(@"<input\b[^>]*>")]
    private static partial Regex InputElement();

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
