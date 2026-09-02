using System.Globalization;
using System.Text;

namespace NativeWeb.Render;

/// <summary>
/// RFC 4180 with the one addition every export needs: a cell a spreadsheet would
/// run as a formula is neutralised so it opens as text (spec 021 R3).
/// </summary>
/// <remarks>
/// <para>
/// Rows end in CRLF. A field holding a comma, a quote, a CR or an LF is quoted and
/// its quotes doubled. No byte-order mark: the response declares
/// <c>charset=utf-8</c>, and a BOM would sit inside the first header cell for every
/// reader that honours the declaration.
/// </para>
/// <para>
/// The formula guard is the OWASP one, a leading apostrophe, applied to a cell
/// whose first printable character is <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c>.
/// "First printable" is the point: importers trim before they sniff, and a
/// zero-width or bidi character in front of the <c>=</c> is invisible to a person
/// and to a first-character check alike, so leading whitespace, control and format
/// characters are skipped first. That skip is also what covers the leading tab, CR
/// and LF on OWASP's list: whatever follows them is what gets judged. A cell that
/// parses as a plain number is exempt, or every negative amount in an export would
/// turn into text.
/// </para>
/// </remarks>
internal static class CsvWriter
{
    private const string RowEnd = "\r\n";

    public static string Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        StringBuilder csv = new();
        AppendRow(csv, headers);

        int number = 0;

        foreach (IReadOnlyList<string> row in rows)
        {
            number++;

            if (row.Count != headers.Count)
            {
                throw new ArgumentException(
                    $"Row {number} has {row.Count} cells for {headers.Count} headers.",
                    nameof(rows));
            }

            AppendRow(csv, row);
        }

        return csv.ToString();
    }

    internal static string Field(string? value)
    {
        string cell = value ?? "";
        bool quote = cell.AsSpan().IndexOfAny(",\"\r\n") >= 0;

        if (LooksLikeAFormula(cell))
        {
            cell = "'" + cell;
            quote = true;
        }

        return quote ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : cell;
    }

    internal static bool LooksLikeAFormula(string cell)
    {
        int i = 0;
        while (i < cell.Length && IsInvisible(cell, i))
        {
            i += char.IsSurrogatePair(cell, i) ? 2 : 1;
        }

        if (i >= cell.Length)
        {
            return false;
        }

        char first = cell[i];
        bool trigger = first is '=' or '+' or '-' or '@';

        return trigger
            && !decimal.TryParse(cell, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
    }

    private static bool IsInvisible(string s, int index)
    {
        UnicodeCategory category = char.GetUnicodeCategory(s, index);

        return category is UnicodeCategory.SpaceSeparator
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator
            or UnicodeCategory.Control
            or UnicodeCategory.Format;
    }

    private static void AppendRow(StringBuilder csv, IReadOnlyList<string> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                csv.Append(',');
            }

            csv.Append(Field(cells[i]));
        }

        csv.Append(RowEnd);
    }
}
