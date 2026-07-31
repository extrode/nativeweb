using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace NativeWeb.Render;

/// <summary>
/// The render target generated template code writes into: UTF-8 straight to an
/// <see cref="IBufferWriter{T}"/> (the response body writer in production), with no
/// intermediate whole-page string (spec 003 R41). Encoding is decided by overload
/// resolution at compile time: <see cref="Write(string?)"/> always HTML-encodes,
/// <see cref="Write(Html)"/> is the only raw path, numbers cannot need encoding.
/// A template expression of any other type fails to compile, which is the intended
/// default for types nobody has made a deliberate encoding decision about.
/// </summary>
public ref struct TemplateWriter(IBufferWriter<byte> output)
{
    private readonly IBufferWriter<byte> _output = output;

    // The characters that must never reach HTML output unencoded. Encoding only
    // these (rather than HtmlEncoder's much larger non-ASCII set) is safe in text
    // and QUOTED-attribute contexts, which are the only contexts templates may
    // interpolate into: an unquoted attribute is terminated by space/=/` and none
    // of those are encoded, and a javascript: value in href/src passes untouched
    // in any HTML encoder — URL values from user data need scheme validation in
    // the calling C#, not encoding here.
    private static readonly System.Buffers.SearchValues<char> CharsNeedingEncoding =
        System.Buffers.SearchValues.Create("<>&\"'");

    // UnicodeRanges.All so non-ASCII text ships as UTF-8 bytes, not numeric
    // entities — matching what the fast path above produces for the same text.
    // The HTML-significant characters are always encoded regardless of ranges.
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>
    /// Writes a static, template-authored UTF-8 chunk verbatim. For generated code
    /// only — application code wanting raw output goes through <see cref="Html.Raw"/>
    /// so every deliberate raw site stays grep-able (audit for both).
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void WriteRaw(scoped ReadOnlySpan<byte> utf8) => _output.Write(utf8);

    /// <summary>Writes a value with HTML encoding. Null renders nothing.</summary>
    public void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        ReadOnlySpan<char> chars = value.AsSpan();
        if (chars.IndexOfAny(CharsNeedingEncoding) < 0)
        {
            WriteUtf16(chars);
            return;
        }

        WriteUtf16(Encoder.Encode(value).AsSpan());
    }

    /// <summary>Writes pre-rendered markup verbatim — the deliberate raw path.</summary>
    public void Write(Html html)
    {
        if (!string.IsNullOrEmpty(html.Markup))
        {
            WriteUtf16(html.Markup.AsSpan());
        }
    }

    /// <summary>Writes a character with HTML encoding (also stops char binding to the int overload).</summary>
    public void Write(char value)
    {
        ReadOnlySpan<char> chars = [value];
        if (chars.IndexOfAny(CharsNeedingEncoding) < 0)
        {
            WriteUtf16(chars);
        }
        else
        {
            WriteUtf16(Encoder.Encode(value.ToString()).AsSpan());
        }
    }

    public void Write(int value)
    {
        // GetSpan(12) always covers int.MinValue's 11 chars, so TryFormat cannot fail.
        Span<byte> scratch = _output.GetSpan(12);
        _ = value.TryFormat(scratch, out int written);
        _output.Advance(written);
    }

    public void Write(long value)
    {
        // GetSpan(24) always covers long.MinValue's 20 chars, so TryFormat cannot fail.
        Span<byte> scratch = _output.GetSpan(24);
        _ = value.TryFormat(scratch, out int written);
        _output.Advance(written);
    }

    private readonly void WriteUtf16(scoped ReadOnlySpan<char> chars)
    {
        while (!chars.IsEmpty)
        {
            // GetSpan(256) always yields >= 256 bytes, so at least one char (max 4
            // UTF-8 bytes) converts per iteration — the loop cannot stall. A
            // DestinationTooSmall result just means "continue with the rest".
            Span<byte> destination = _output.GetSpan(256);
            Utf8.FromUtf16(chars, destination, out int charsRead, out int bytesWritten, replaceInvalidSequences: true, isFinalBlock: true);
            _output.Advance(bytesWritten);
            chars = chars[charsRead..];
        }
    }
}
