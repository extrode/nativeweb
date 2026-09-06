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

        MatchCollection addenda = SpecStatusLine().Matches(File.ReadAllText(Path.Combine(root, Spec)));

        // Exactly one, not the first one. Taking the first would read a superseded addendum and
        // agree with a runbook the newest one contradicts -- which is the single-file drift this
        // whole test exists to catch, passing green. A second status line is a reconciliation
        // someone has to do by hand, so it fails here rather than being resolved by a rule.
        Assert.True(
            addenda.Count == 1,
            $"{Spec} carries {addenda.Count} `**R.. are met. R.. are not.**` status lines; this reads one.");

        HashSet<string> fromSpec = [];
        foreach (Match id in RequirementId().Matches(addenda[0].Groups["unmet"].Value))
        {
            fromSpec.Add(id.Value);
        }

        Assert.NotEmpty(fromRunbook);
        Assert.Equal(
            string.Join(", ", fromSpec.Order()),
            string.Join(", ", fromRunbook.Order()));
    }

    [Fact]
    public void RunbookMeasuredTables_ReportAValueForEveryMeasure()
    {
        string root = RepoRoot();
        string[] lines = File.ReadAllLines(Path.Combine(root, Runbook));

        // A digit, not the absence of one placeholder spelling. `NOT YET MEASURED` was the
        // spelling this repository happened to use; `TBD`, an em dash and an empty cell are the
        // same defect and all three pass an assertion that only knows the one string.
        List<string> unmeasured = [];
        int tables = 0;
        int rowsInTable = 0;
        bool inTable = false;

        for (int i = 0; i <= lines.Length; i++)
        {
            string line = i < lines.Length ? lines[i].Trim() : string.Empty;
            bool ends = i == lines.Length || !line.StartsWith('|');

            if (inTable && ends)
            {
                Assert.True(
                    rowsInTable >= 2,
                    $"{Runbook}: a measured table ending at line {i} has {rowsInTable} rows; a table this thin has had rows deleted rather than measured.");
                inTable = false;
            }

            if (i == lines.Length)
            {
                break;
            }

            if (MeasuredTableHeader().IsMatch(line))
            {
                inTable = true;
                rowsInTable = 0;
                tables++;
                continue;
            }

            if (!inTable || !line.StartsWith('|'))
            {
                continue;
            }

            string[] cells = line.Split('|');
            if (cells.Length < 3 || cells[1].TrimStart().StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            rowsInTable++;
            string value = cells[2].Trim();
            if (!value.Any(char.IsDigit))
            {
                unmeasured.Add($"line {i + 1}: {cells[1].Trim()} = {(value.Length == 0 ? "<empty>" : value)}");
            }
        }

        Assert.Empty(unmeasured);
        Assert.True(
            tables >= 3,
            $"{Runbook} has {tables} measured tables; the release, restore and recovery ones are expected.");
    }

    [Fact]
    public void NoDocument_SaysTheDeploymentReferenceStillReadsUnmeasured()
    {
        string root = RepoRoot();

        List<string> stale = [];
        foreach (string path in MarkdownFiles(root))
        {
            // Scanned per paragraph, not per line. Both claims this was written for wrap:
            // `003-framework-readiness.md` had "the deployment reference" ending one line and
            // "reads `NOT YET MEASURED`" opening the next, so a line-at-a-time sweep reports
            // a clean document and the guard asserts nothing.
            foreach (string paragraph in Paragraphs(File.ReadAllText(path)))
            {
                // The placeholder's own name is not the defect -- a sentence reporting that it is
                // gone has to be able to quote it, and both `deployment.md` and `milestones.md`
                // do. The defect is a present-tense claim that the document still reads that way,
                // so the reading verb is what this matches on. `read` is deliberately absent from
                // that verb list: it is also the past tense, and "the runbook read NOT YET
                // MEASURED until 2026-09-05" is a true sentence this must not convict.
                if (NamesTheRunbook().IsMatch(paragraph)
                    && Placeholder().IsMatch(paragraph)
                    && PresentTenseReading().IsMatch(paragraph))
                {
                    stale.Add($"{Path.GetRelativePath(root, path).Replace('\\', '/')}: {Collapse(paragraph)}");
                }
            }
        }

        Assert.Empty(stale);
    }

    /// <summary>
    /// Every markdown file that describes the present, so a stale claim cannot escape by living
    /// outside `docs/`.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Append-only records are absent rather than exempted, on the same distinction
    /// <see cref="DocsClaimsTests"/> draws for `breaking-changes.md`: a record of what happened
    /// has to be able to say the runbook once read `NOT YET MEASURED`, and a version of
    /// `CHANGELOG.md` that could not would be a broken changelog. `work/done/` and
    /// `work/archive/` are the same kind of document, and `work/status/` is deliberately not --
    /// it describes now.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> MarkdownFiles(string root)
    {
        string[] skipSegments = [".git", ".worktrees", "tmp", "bin", "obj", "artifacts", "docs-site"];
        string[] records = ["CHANGELOG.md", "work/done", "work/archive"];

        foreach (string path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Split('/').Any(segment => skipSegments.Contains(segment)))
            {
                continue;
            }

            if (records.Any(r => relative == r || relative.StartsWith(r + "/", StringComparison.Ordinal)))
            {
                continue;
            }

            yield return path;
        }
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
            // A block quote's own markers are stripped first. Without that a bare `>` never reads
            // as blank and `> - item` never reads as a bullet, so `deployment.md`'s 37-line
            // opening quote is one block -- and one reading verb anywhere in it would convict the
            // sentence that reports the placeholders are gone.
            string text = BlockQuoteMarker().Replace(raw, string.Empty);
            bool breaks =
                text.Trim().Length == 0
                || BulletStart().IsMatch(text)
                || text.TrimStart().StartsWith('|');
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

    [GeneratedRegex(@"^\s*([-*+]|\d{1,2}\.)\s")]
    private static partial Regex BulletStart();

    [GeneratedRegex(@"^\s*(>\s?)+")]
    private static partial Regex BlockQuoteMarker();

    [GeneratedRegex(@"^\|\s*Measure\s*\|\s*Value\s*\|\s*$")]
    private static partial Regex MeasuredTableHeader();

    [GeneratedRegex(@"deployment\.md|deployment reference|deployment runbook|the runbook", RegexOptions.IgnoreCase)]
    private static partial Regex NamesTheRunbook();

    [GeneratedRegex(@"not yet measured", RegexOptions.IgnoreCase)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\b(reads|says|shows|carries|states|contains|has|have)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PresentTenseReading();
}
