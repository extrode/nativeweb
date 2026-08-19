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
