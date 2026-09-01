using System.Globalization;

using Microsoft.AspNetCore.Antiforgery;

namespace NativeWeb.Scaffold;

/// <summary>
/// Form-value conversion and display formatting for generated scaffolding code.
/// </summary>
/// <remarks>
/// Lives here rather than in the emitted source so the generator emits a call
/// instead of a parser, and so this is unit-testable on its own. Everything is
/// invariant-culture: a form field is a wire format, not a locale-formatted
/// display string, and parsing one with the ambient culture is how a date becomes
/// a different date on a machine in another country.
/// </remarks>
public static class ScaffoldValues
{
    /// <summary>Trimmed, or null when the field was absent or blank.</summary>
    public static string? Text(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw!.Trim();

    public static short Int16(string? raw, short fallback = 0) =>
        short.TryParse(Text(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out short value) ? value : fallback;

    public static short? NullableInt16(string? raw) =>
        short.TryParse(Text(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out short value) ? value : null;

    public static int Int32(string? raw, int fallback = 0) =>
        int.TryParse(Text(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    public static int? NullableInt32(string? raw) =>
        int.TryParse(Text(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    public static long Int64(string? raw, long fallback = 0) =>
        long.TryParse(Text(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : fallback;

    public static long? NullableInt64(string? raw) =>
        long.TryParse(Text(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;

    public static decimal Decimal(string? raw, decimal fallback = 0m) =>
        decimal.TryParse(Text(raw), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value : fallback;

    public static decimal? NullableDecimal(string? raw) =>
        decimal.TryParse(Text(raw), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value : null;

    public static double Double(string? raw, double fallback = 0d) =>
        double.TryParse(Text(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;

    public static double? NullableDouble(string? raw) =>
        double.TryParse(Text(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

    public static float Single(string? raw, float fallback = 0f) =>
        float.TryParse(Text(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    public static float? NullableSingle(string? raw) =>
        float.TryParse(Text(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : null;

    /// <summary>
    /// An HTML checkbox that is unticked submits nothing at all, so absence is
    /// false. A ticked one submits "on" unless the markup says otherwise.
    /// </summary>
    public static bool Boolean(string? raw) =>
        Text(raw) is string s
        && (s.Equals("on", StringComparison.OrdinalIgnoreCase)
            || s.Equals("true", StringComparison.OrdinalIgnoreCase)
            || s == "1");

    public static DateTime DateTimeValue(string? raw, DateTime fallback) =>
        DateTime.TryParse(Text(raw), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value)
            ? value
            : fallback;

    public static DateTime? NullableDateTime(string? raw) =>
        DateTime.TryParse(Text(raw), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value)
            ? value
            : null;

    public static Guid GuidValue(string? raw) =>
        Guid.TryParse(Text(raw), out Guid value) ? value : Guid.Empty;

    public static Guid? NullableGuid(string? raw) =>
        Guid.TryParse(Text(raw), out Guid value) ? value : null;

    // The validators the generated FromForm calls before Build converts anything.
    //
    // They exist because every converter above answers malformed input with a
    // fallback, and a fallback is a silent wrong answer: "abc" in a number field
    // stored 0, "not-a-date" stored this instant, "xyz" in a Guid field stored
    // Guid.Empty, and the form redirected as if the submission had been accepted.
    // The <input type="number"> the generator emits stops a browser sending that,
    // which is why nobody saw it -- an API client, a hand-built request or a
    // disabled-JS submission is under no such constraint.
    //
    // Blank is NOT malformed here: an absent field keeps the documented fallback,
    // which is what the non-nullable converters have always done and what the
    // required-field check on strings is for. The claim these make is narrower --
    // a value that was submitted and cannot be read as the declared type is an
    // error the user is told about, never a number the store keeps.
    public static bool IsInt16(string? raw) => Blank(raw) || short.TryParse(Text(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    public static bool IsInt32(string? raw) => Blank(raw) || int.TryParse(Text(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    public static bool IsInt64(string? raw) => Blank(raw) || long.TryParse(Text(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    public static bool IsDecimal(string? raw) => Blank(raw) || decimal.TryParse(Text(raw), NumberStyles.Number, CultureInfo.InvariantCulture, out _);

    public static bool IsDouble(string? raw) => Blank(raw) || double.TryParse(Text(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    public static bool IsSingle(string? raw) => Blank(raw) || float.TryParse(Text(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    public static bool IsDateTime(string? raw) =>
        Blank(raw)
        || DateTime.TryParse(Text(raw), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _);

    public static bool IsGuid(string? raw) => Blank(raw) || Guid.TryParse(Text(raw), out _);

    private static bool Blank(string? raw) => Text(raw) is null;

    /// <summary>Invariant display form for a list cell or a form input's value.</summary>
    public static string Display(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "Yes" : "No",
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>The value form for an <c>&lt;input value="..."&gt;</c>, which is not always the display form.</summary>
    public static string Editable(object? value) => value switch
    {
        null => "",
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
        bool b => b ? "on" : "",
        _ => Display(value),
    };

    /// <summary>
    /// The ordering a list request resolved to, having rejected anything the model
    /// did not declare sortable.
    /// </summary>
    /// <remarks>
    /// This is where R5 is met, and it is met by falling back rather than by
    /// answering 400: a bookmarked link to a column somebody has since removed from
    /// the model should show the list, not an error. The rejected name is never
    /// returned, so it cannot reach a store and cannot reach SQL.
    /// </remarks>
    /// <param name="property">The requested property name, from the query string.</param>
    /// <param name="direction">The requested direction, from the query string.</param>
    /// <param name="sortFields">The model's declared sortable property names.</param>
    /// <param name="fallback">The key property, used when the request names nothing usable.</param>
    /// <returns>A spec naming a declared property.</returns>
    public static ScaffoldSortSpec Sort(
        string? property,
        string? direction,
        IReadOnlyList<string> sortFields,
        string fallback)
    {
        string name = fallback;

        // Absent is not the same as unrecognised. No ?sort= at all is a request for the
        // default ordering, and ?dir=desc still applies to it; only a NAMED column that
        // is not sortable takes the direction down with it.
        string? requested = Text(property);
        bool matched = requested is null;

        if (requested is not null)
        {
            for (int i = 0; i < sortFields.Count; i++)
            {
                if (string.Equals(sortFields[i], requested, StringComparison.Ordinal))
                {
                    name = requested;
                    matched = true;
                    break;
                }
            }
        }

        // The whole request falls back, direction included. A ?dir= applied to a column
        // the reader did not get is an ordering nobody asked for, and "the key
        // ascending" is what the documented fallback says and what a client that
        // mistyped a column name should be able to predict.
        ScaffoldSortDirection dir = matched
            && Text(direction) is string d
            && d.Equals("desc", StringComparison.OrdinalIgnoreCase)
                ? ScaffoldSortDirection.Descending
                : ScaffoldSortDirection.Ascending;

        return new ScaffoldSortSpec(name, dir);
    }

    /// <summary>The position a list request resolved to, or null to start at the beginning.</summary>
    /// <remarks>
    /// Null for anything that is not a cursor this release wrote, and null for one
    /// written against a different ordering than the one now in force. Both are R9:
    /// an edited or stale <c>?after=</c> renders the first page instead of an error.
    /// </remarks>
    /// <param name="raw">The raw query-string value.</param>
    /// <param name="sort">The ordering this request resolved to.</param>
    /// <returns>A usable cursor, or null.</returns>
    public static ScaffoldCursor? Cursor(string? raw, ScaffoldSortSpec sort) =>
        ScaffoldCursor.TryDecode(raw, out ScaffoldCursor cursor) && cursor.Matches(sort)
            ? cursor
            : null;

    /// <summary>
    /// A sort value in the round-trippable form a cursor carries, or null when the
    /// value is null.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Display"/>, and the difference matters. Display formats a
    /// <see cref="DateTime"/> to the minute and a bool to "Yes", both of which lose
    /// information a page boundary is defined by: two rows a second apart would
    /// compare equal, so paging over a timestamp column would repeat or skip whatever
    /// sat on the boundary. Round-trip form throughout -- "o" for a timestamp,
    /// invariant for a number -- so what the store parses back is the value it wrote.
    /// </remarks>
    /// <param name="value">The row's value for the sorted property.</param>
    /// <returns>Text the store can parse back, or null.</returns>
    public static string? CursorText(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "1" : "0",
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}

/// <summary>
/// Endpoint metadata that makes <c>UseAntiforgery()</c> validate a route whose
/// handler does not take <c>[FromForm]</c> parameters.
/// </summary>
/// <remarks>
/// Measured, not assumed (brainstorm 001 sec.12): <c>[FromForm]</c> is what
/// normally attaches this metadata, so a handler binding through
/// <c>IBindableFromHttpContext</c> has none and <c>UseAntiforgery()</c> skips the
/// route entirely -- a tokenless POST answered 200 OK. The framework's own
/// <c>AntiforgeryMetadata</c> is internal; the interface is public, so the
/// generator attaches this instead. It is emitted on every scaffolded POST, which
/// is the point: the guard cannot be forgotten because nobody writes it.
/// </remarks>
public sealed class RequiresAntiforgery : IAntiforgeryMetadata
{
    /// <summary>The single instance; it carries no state.</summary>
    public static readonly RequiresAntiforgery Instance = new();

    private RequiresAntiforgery()
    {
    }

    /// <inheritdoc/>
    public bool RequiresValidation => true;
}
