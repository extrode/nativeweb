using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The naming rules in <c>.editorconfig</c> are actually enforced, and every one of them is
/// wired to a symbol spec and a style that exist.
/// </summary>
/// <remarks>
/// <para>
/// The failure mode this exists for is silence, not breakage. Measured 2026-08-18: a section
/// full of <c>dotnet_naming_rule</c> entries at <c>severity = warning</c> reports nothing at
/// build time on its own -- a field renamed to <c>BadlyNamedLines</c> built clean. IDE1006 has
/// to be switched on separately; the per-rule severity decides how loud a violation is, not
/// whether the analyzer runs. So the rules had been decorative since the file was written, and
/// nothing said so, which is exactly what would happen again if someone dropped the line while
/// tidying.
/// </para>
/// <para>
/// The dangling-reference half is the other quiet failure: a rule naming a symbol spec or style
/// that does not exist is not an error anywhere, it just never matches.
/// </para>
/// </remarks>
public class EditorConfigNamingTests
{
    [Fact]
    public void NamingViolations_AreReportedAtBuildTime()
    {
        string severity = Settings()
            .GetValueOrDefault("dotnet_diagnostic.IDE1006.severity", "none");

        Assert.True(
            severity is "warning" or "error",
            $"IDE1006 is '{severity}' in .editorconfig, so every naming rule in it is decorative: "
            + "the analyzer does not run and a violation builds clean.");
    }

    [Fact]
    public void EveryNamingRule_PointsAtASymbolSpecAndAStyleThatExist()
    {
        Dictionary<string, string> settings = Settings();

        string[] rules = [.. settings.Keys
            .Where(k => k.StartsWith("dotnet_naming_rule.", StringComparison.Ordinal)
                && k.EndsWith(".severity", StringComparison.Ordinal))
            .Select(k => k["dotnet_naming_rule.".Length..^".severity".Length])];

        // Anti-vacuity: an empty parse must fail rather than report green. Eight rules at the
        // 2026-08-18 measurement.
        Assert.True(rules.Length >= 6, $"Parsed only {rules.Length} naming rules from .editorconfig.");

        List<string> dangling = [];
        foreach (string rule in rules)
        {
            string symbols = settings[$"dotnet_naming_rule.{rule}.symbols"];
            string style = settings[$"dotnet_naming_rule.{rule}.style"];

            if (!settings.ContainsKey($"dotnet_naming_symbols.{symbols}.applicable_kinds"))
            {
                dangling.Add($"{rule} -> symbols '{symbols}'");
            }

            if (!settings.ContainsKey($"dotnet_naming_style.{style}.capitalization"))
            {
                dangling.Add($"{rule} -> style '{style}'");
            }
        }

        Assert.True(
            dangling.Count == 0,
            "These naming rules reference something .editorconfig does not define, so they "
            + "silently never match: " + string.Join(", ", dangling));
    }

    // The [*.cs] section only. A key in [*] would apply to C# too, but nothing puts naming
    // settings there and reading both would let a wrongly-scoped key pass as correctly scoped.
    private static Dictionary<string, string> Settings()
    {
        Dictionary<string, string> settings = new(StringComparer.Ordinal);
        bool inCSharp = false;

        foreach (string raw in File.ReadAllLines(Path.Combine(RepoRoot(), ".editorconfig")))
        {
            string line = raw.Trim();
            if (line.StartsWith('['))
            {
                inCSharp = line == "[*.cs]";
                continue;
            }

            if (!inCSharp || line.Length == 0 || line.StartsWith('#') || !line.Contains('='))
            {
                continue;
            }

            int at = line.IndexOf('=');
            settings[line[..at].Trim()] = line[(at + 1)..].Trim();
        }

        Assert.NotEmpty(settings);
        return settings;
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
