using System.Text.RegularExpressions;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <summary>
/// The deployment runbook and spec 011 report the same requirement status, and no
/// document says a figure is unmeasured that has been measured.
/// </summary>
/// <remarks>
/// <para>
/// Both failures this guards have happened. A fable review on 2026-09-06 found the
/// spec's acceptance addendum reporting R14 met while the runbook's gap table
/// reported it unmet, because R17 was split in one file and not the other -- two
/// documents describing one deployment, drifting apart in the pass that edited one
/// of them. Separately, `performance.md` and `003-framework-readiness.md` both still
/// said every elapsed time in the deployment reference read `NOT YET MEASURED`,
/// which stopped being true when T15 ran on 2026-09-05.
/// </para>
/// <para>
/// The parity assertion reads the gap table's unstruck rows, because a struck row is
/// a closed gap and names the requirement it closed. Nothing here asserts which
/// requirements are unmet -- only that the two documents answer the same way, which
/// is the fact a single-file edit breaks.
/// </para>
/// </remarks>
public sealed partial class RunbookClaimsTests
{
    private const string Runbook = "docs/02-reference/deployment.md";
    private const string Spec = "docs/specs/011-production-deployment/011-spec.md";

    [Fact]
    public void RunbookGapTable_AgreesWithTheSpecOnWhichRequirementsAreUnmet()
    {
        string root = RepoRoot();

        HashSet<string> fromRunbook = [];
        foreach (string row in GapTableRows(File.ReadAllText(Path.Combine(root, Runbook))))
        {
            foreach (Match id in RequirementId().Matches(row))
            {
                fromRunbook.Add(id.Value);
            }
        }

        Match addendum = SpecStatusLine().Match(File.ReadAllText(Path.Combine(root, Spec)));
        Assert.True(
            addendum.Success,
            $"{Spec} no longer carries an `**R.. are met. R.. are not.**` status line for this to read.");

        HashSet<string> fromSpec = [];
        foreach (Match id in RequirementId().Matches(addendum.Groups["unmet"].Value))
        {
            fromSpec.Add(id.Value);
        }

        Assert.NotEmpty(fromRunbook);
        Assert.Equal(
            string.Join(", ", fromSpec.Order()),
            string.Join(", ", fromRunbook.Order()));
    }

    [Fact]
    public void RunbookMeasuredTables_CarryNoUnfilledPlaceholder()
    {
        string root = RepoRoot();
        string[] lines = File.ReadAllLines(Path.Combine(root, Runbook));

        int tables = 0;
        bool inTable = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line == "| Measure | Value |")
            {
                inTable = true;
                tables++;
                continue;
            }

            if (!inTable)
            {
                continue;
            }

            if (!line.StartsWith('|'))
            {
                inTable = false;
                continue;
            }

            Assert.DoesNotContain(
                "NOT YET",
                line,
                StringComparison.Ordinal);
        }

        Assert.True(tables >= 3, $"{Runbook} has {tables} measured tables; the release, restore and recovery ones are expected.");
    }

    [Fact]
    public void NoDocument_SaysTheDeploymentReferenceStillReadsUnmeasured()
    {
        string root = RepoRoot();

        List<string> stale = [];
        foreach (string dir in (string[])["docs", "work"])
        {
            foreach (string path in Directory.EnumerateFiles(Path.Combine(root, dir), "*.md", SearchOption.AllDirectories))
            {
                // Scanned per paragraph, not per line. Both claims this was written for wrap:
                // `003-framework-readiness.md` had "the deployment reference" ending one line and
                // "reads `NOT YET MEASURED`" opening the next, so a line-at-a-time sweep reports
                // a clean document and the guard asserts nothing.
                foreach (string paragraph in Paragraphs(File.ReadAllText(path)))
                {
                    bool namesTheRunbook =
                        paragraph.Contains("deployment.md", StringComparison.Ordinal)
                        || paragraph.Contains("deployment reference", StringComparison.Ordinal);

                    // The placeholder's own name is not the defect -- a sentence reporting that it
                    // is gone has to be able to quote it, and both `deployment.md` and
                    // `milestones.md` do. The defect is a present-tense claim that the document
                    // still reads that way, so the reading verb is what this matches on.
                    if (namesTheRunbook
                        && paragraph.Contains("NOT YET MEASURED", StringComparison.Ordinal)
                        && PresentTenseReading().IsMatch(paragraph))
                    {
                        stale.Add($"{Path.GetRelativePath(root, path).Replace('\\', '/')}: {Collapse(paragraph)}");
                    }
                }
            }
        }

        Assert.Empty(stale);
    }

    /// <summary>
    /// Blank-line separated blocks, with each bullet and each table row its own block.
    /// </summary>
    /// <remarks>
    /// A list is not one paragraph and neither is a table: `work/milestones.md` is a single
    /// unbroken table, so without the row break every milestone reads as one block and any
    /// reading verb anywhere in it convicts every other row.
    /// </remarks>
    private static IEnumerable<string> Paragraphs(string document)
    {
        List<string> block = [];
        foreach (string raw in document.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            bool breaks =
                raw.Trim().Length == 0
                || BulletStart().IsMatch(raw)
                || raw.TrimStart().StartsWith('|');
            if (breaks && block.Count > 0)
            {
                yield return string.Join('\n', block);
                block.Clear();
            }

            if (raw.Trim().Length > 0)
            {
                block.Add(raw);
            }
        }

        if (block.Count > 0)
        {
            yield return string.Join('\n', block);
        }
    }

    private static string Collapse(string paragraph) =>
        string.Join(' ', paragraph.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

    private static IEnumerable<string> GapTableRows(string runbook)
    {
        bool inTable = false;
        foreach (string raw in runbook.Split('\n'))
        {
            string line = raw.Trim();
            if (line == "| Gap | Status |")
            {
                inTable = true;
                continue;
            }

            if (!inTable)
            {
                continue;
            }

            if (!line.StartsWith('|'))
            {
                yield break;
            }

            string[] cells = line.Split('|');
            if (cells.Length < 3 || cells[1].TrimStart().StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            if (cells[1].TrimStart().StartsWith("~~", StringComparison.Ordinal))
            {
                continue;
            }

            yield return line;
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

    [GeneratedRegex(@"\bR\d+[ab]?\b")]
    private static partial Regex RequirementId();

    [GeneratedRegex(@"are met\.\s*(?<unmet>[^*]+?)\s*are not\.\*\*")]
    private static partial Regex SpecStatusLine();

    [GeneratedRegex(@"^\s*([-*+]|\d+\.)\s")]
    private static partial Regex BulletStart();

    [GeneratedRegex(@"\b(still )?(reads|read|says|shows|carries)\b")]
    private static partial Regex PresentTenseReading();
}
