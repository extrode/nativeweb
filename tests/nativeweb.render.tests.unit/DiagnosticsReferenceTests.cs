using System.Text.RegularExpressions;

using Xunit;

namespace NativeWeb.Render.Tests.Unit;

/// <summary>
/// docs/template-diagnostics.md is the only place an NWT id is explained rather
/// than merely emitted, so it is worth nothing the moment it drifts from the
/// descriptors — and a reference page drifts silently, because nobody reads it
/// on the build that added the seventeenth diagnostic.
///
/// Both files are read from the repo rather than from a fixture on purpose. A
/// copy of the descriptor list in this file would be a third thing to keep in
/// step, which is the bug rather than the check for it.
/// </summary>
public sealed partial class DiagnosticsReferenceTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NativeWeb.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException($"Could not locate NativeWeb.slnx above {AppContext.BaseDirectory}.");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    // Matches the `"NWT001", "Malformed template header",` pair each descriptor
    // opens with. Anchored on the id so a message string mentioning an id in
    // passing cannot be mistaken for a declaration.
    [GeneratedRegex("\"(NWT\\d{3})\", \"([^\"]+)\"")]
    private static partial Regex DescriptorPattern();

    // Any NWT id mentioned anywhere in the reference, declaration or prose.
    [GeneratedRegex("NWT\\d{3}")]
    private static partial Regex AnyIdPattern();

    private static Dictionary<string, string> Descriptors()
    {
        string source = Read("libs", "nativeweb.render.generator", "TemplateDiagnostics.cs");
        Dictionary<string, string> found = new(StringComparer.Ordinal);
        foreach (Match match in DescriptorPattern().Matches(source))
        {
            found[match.Groups[1].Value] = match.Groups[2].Value;
        }

        return found;
    }

    [Fact]
    public void EveryDescriptorHasARowInTheReference()
    {
        string reference = Read("docs", "template-diagnostics.md");
        Dictionary<string, string> descriptors = Descriptors();

        // Guards the guard: a regex that matched nothing would make every
        // assertion below vacuously true.
        Assert.True(descriptors.Count >= 16, $"Found only {descriptors.Count} descriptors — the pattern stopped matching.");

        var missing = descriptors.Keys
            .Where(id => !reference.Contains($"| {id} | ", StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0, $"docs/template-diagnostics.md has no at-a-glance row for: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryReferenceRowUsesTheDescriptorsOwnTitle()
    {
        string reference = Read("docs", "template-diagnostics.md");

        List<string> wrong = [];
        foreach (KeyValuePair<string, string> descriptor in Descriptors())
        {
            if (!reference.Contains($"| {descriptor.Key} | {descriptor.Value} |", StringComparison.Ordinal))
            {
                wrong.Add($"{descriptor.Key} (expected title '{descriptor.Value}')");
            }
        }

        Assert.True(wrong.Count == 0, $"docs/template-diagnostics.md rows disagree with the descriptor titles: {string.Join("; ", wrong)}");
    }

    [Fact]
    public void TheReferenceInventsNoDiagnosticThatDoesNotExist()
    {
        string reference = Read("docs", "template-diagnostics.md");
        Dictionary<string, string> descriptors = Descriptors();

        var invented = AnyIdPattern().Matches(reference)
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !descriptors.ContainsKey(id))
            .ToList();

        Assert.True(invented.Count == 0, $"docs/template-diagnostics.md documents ids with no descriptor: {string.Join(", ", invented)}");
    }
}
