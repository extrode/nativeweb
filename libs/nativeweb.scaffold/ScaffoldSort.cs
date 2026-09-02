using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Text;
using System.Text.Unicode;

namespace NativeWeb.Scaffold;

/// <summary>Which way a scaffolded list is ordered.</summary>
public enum ScaffoldSortDirection
{
    /// <summary>Smallest first.</summary>
    Ascending,

    /// <summary>Largest first.</summary>
    Descending,
}

/// <summary>An ordering the generated list has already validated.</summary>
/// <remarks>
/// <see cref="Property"/> is a declared property name that was in the model's
/// <c>[ScaffoldSort]</c> set when this was constructed. The generated endpoint is
/// what checks that, before the store is called, so a store receiving one of these
/// does not re-validate -- and a store handed a name it does not recognise should
/// fall back to the key order rather than throw, because the only way that happens
/// is a hand-written caller.
/// </remarks>
/// <param name="Property">The declared property name to order by.</param>
/// <param name="Direction">Which way.</param>
public readonly record struct ScaffoldSortSpec(string Property, ScaffoldSortDirection Direction);

/// <summary>
/// A position in a sorted list: the sort value and key of the row a page ended on,
/// and which way the reader is travelling from it.
/// </summary>
/// <remarks>
/// Keyset paging, not offset paging. A page is "the rows after this one in this
/// order", so a row inserted or deleted between two requests shifts nothing: with
/// <c>OFFSET</c>, an insert ahead of the reader pushes one row from the page they
/// are on to the page they are about to request, and they never see it.
/// <para>
/// <b><see cref="SortValue"/> is a string the framework does not interpret.</b> Only
/// the store knows whether a column is a <c>TIMESTAMPTZ</c>, an <c>INTEGER</c> or
/// text, so it is the store that parses it and the store that decides a value it
/// cannot parse means "start from the beginning". The alternative -- a typed cursor
/// -- puts every model's property types in front of the framework at run time, which
/// is reflection, which is not NativeAOT-clean. Null means the row's sorted column
/// was SQL NULL, which is a distinct position from the empty string and has to
/// survive the round trip: a nullable column ordered <c>NULLS LAST</c> keeps its
/// NULL rows in one contiguous block, and a cursor that could not tell "" from NULL
/// would re-enter that block from its start and repeat every row in it.
/// </para>
/// <para>
/// Opaque by construction rather than by obscurity: the encoding is a private binary
/// layout behind Base64Url, so there is no documented shape for a client to build one
/// from. It is <b>not</b> a security boundary and is not signed. A forged cursor can
/// only reposition a client inside rows it was already permitted to list, and its
/// values reach the database as parameters, never as SQL. What the framework does
/// guarantee is that a hostile string cannot crash the list: see
/// <see cref="TryDecode"/>.
/// </para>
/// </remarks>
/// <param name="Property">The property the list was ordered by when this was issued.</param>
/// <param name="Direction">The direction it was ordered in.</param>
/// <param name="SortValue">That property's value on the boundary row, as text, or null for SQL NULL.</param>
/// <param name="Key">The boundary row's key, which breaks ties.</param>
/// <param name="Backward">
/// True to walk towards the start of the order rather than away from it. The store
/// mirrors its comparison and returns the rows <i>before</i> the boundary, still in
/// display order.
/// </param>
public readonly record struct ScaffoldCursor(
    string Property,
    ScaffoldSortDirection Direction,
    string? SortValue,
    string Key,
    bool Backward)
{
    // Bumped if the layout changes. A cursor written by an older release then fails
    // TryDecode rather than being read as something it is not, and a stale link
    // renders the first page -- which is R9's behaviour anyway, so a deploy mid-read
    // costs a reader their position and nothing else.
    private const byte Version = 1;

    // A length prefix cannot legitimately reach this, because both caps below are far
    // smaller, so it is free to mean "this value was null".
    private const ushort NullMarker = ushort.MaxValue;

    // Each cap is generous for what it holds and small enough that a decoded cursor
    // cannot be used to make the process allocate: a key or a formatted timestamp is
    // tens of bytes, and a property name is bounded by identifier length in practice.
    private const int MaxPropertyBytes = 128;
    private const int MaxValueBytes = 512;
    private const int MaxDecodedBytes = 2 + (3 * 2) + MaxPropertyBytes + (2 * MaxValueBytes);

    // Checked before decoding, so an oversized string is rejected without a buffer
    // being touched for it.
    private const int MaxEncodedChars = 2048;

    private const byte FlagDescending = 1;
    private const byte FlagBackward = 2;
    private const byte FlagMask = FlagDescending | FlagBackward;

    /// <summary>The opaque form, for a link.</summary>
    /// <remarks>
    /// Returns the empty string when any part is too long to encode, or is not
    /// well-formed UTF-16. A caller getting one back emits no link, which costs the
    /// reader one Previous/Next button on a row whose sort value is half a kilobyte --
    /// against the alternative of throwing while rendering a page that is otherwise
    /// fine, or of a token that decodes to a cursor this cursor is not.
    /// </remarks>
    /// <returns>A base64url token, or the empty string.</returns>
    public string Encode()
    {
        int propertyBytes = Encoding.UTF8.GetByteCount(this.Property);
        int valueBytes = this.SortValue is null ? 0 : Encoding.UTF8.GetByteCount(this.SortValue);
        int keyBytes = Encoding.UTF8.GetByteCount(this.Key);

        if (propertyBytes > MaxPropertyBytes || valueBytes > MaxValueBytes || keyBytes > MaxValueBytes)
        {
            return string.Empty;
        }

        Span<byte> payload = stackalloc byte[MaxDecodedBytes];
        payload[0] = Version;
        payload[1] = (byte)((this.Direction == ScaffoldSortDirection.Descending ? FlagDescending : 0)
            | (this.Backward ? FlagBackward : 0));

        int at = 2;
        if (!TryWrite(payload, ref at, this.Property, propertyBytes)
            || !TryWrite(payload, ref at, this.SortValue, valueBytes)
            || !TryWrite(payload, ref at, this.Key, keyBytes))
        {
            return string.Empty;
        }

        return Base64Url.EncodeToString(payload[..at]);
    }

    /// <summary>Reads a cursor back, or reports that it could not.</summary>
    /// <remarks>
    /// <b>Never throws.</b> The argument is whatever was in the query string, so every
    /// malformed shape has to be a false rather than an exception: null, empty,
    /// over-length, not base64url, truncated, a version this release does not know, an
    /// unknown flag bit, a length prefix that runs past the end, a value that is not
    /// well-formed UTF-8, and trailing bytes after the third value. The caller renders
    /// the first page, which is what a reader who edited the URL should get.
    /// <para>
    /// Accepting is exact: a token this returns true for re-encodes to itself. That is
    /// what makes "a cursor this release wrote" a claim rather than a hope, and it is
    /// why the UTF-8 read above is strict.
    /// </para>
    /// </remarks>
    /// <param name="value">The raw query-string value.</param>
    /// <param name="cursor">The decoded cursor, or default.</param>
    /// <returns>True when <paramref name="value"/> was a cursor this release wrote.</returns>
    public static bool TryDecode(string? value, out ScaffoldCursor cursor)
    {
        cursor = default;

        if (string.IsNullOrEmpty(value) || value!.Length > MaxEncodedChars)
        {
            return false;
        }

        // IsValid first, because TryDecodeFromChars is "try" about the destination
        // buffer only: handed a character outside the alphabet, or a length that
        // cannot be a base64url payload, it throws FormatException. The argument here
        // is a query-string value, so that would be a 500 on an edited URL.
        Span<byte> payload = stackalloc byte[MaxDecodedBytes];
        if (!Base64Url.IsValid(value.AsSpan())
            || !Base64Url.TryDecodeFromChars(value.AsSpan(), payload, out int decoded))
        {
            return false;
        }

        if (decoded < 2 || payload[0] != Version || (payload[1] & ~FlagMask) != 0)
        {
            return false;
        }

        ReadOnlySpan<byte> body = payload[..decoded];
        int at = 2;

        if (!TryRead(body, ref at, MaxPropertyBytes, out string? property)
            || property is null
            || !TryRead(body, ref at, MaxValueBytes, out string? sortValue)
            || !TryRead(body, ref at, MaxValueBytes, out string? key)
            || key is null
            || at != decoded)
        {
            return false;
        }

        cursor = new ScaffoldCursor(
            property,
            (payload[1] & FlagDescending) != 0 ? ScaffoldSortDirection.Descending : ScaffoldSortDirection.Ascending,
            sortValue,
            key,
            (payload[1] & FlagBackward) != 0);

        return true;
    }

    /// <summary>True when this cursor was issued for the ordering now in force.</summary>
    /// <remarks>
    /// A reader who changes the sort while holding a position has a position that means
    /// nothing in the new order -- "after Smith" says nothing about an order by date.
    /// The generated list calls this and drops a cursor that disagrees, which is why
    /// changing the sort lands on the first page rather than somewhere arbitrary.
    /// </remarks>
    /// <param name="sort">The ordering the current request resolved to.</param>
    /// <returns>True when the cursor may be used.</returns>
    public bool Matches(ScaffoldSortSpec sort) =>
        this.Direction == sort.Direction
        && string.Equals(this.Property, sort.Property, StringComparison.Ordinal);

    // Strict, for the reason TryRead is: Encoding.UTF8.GetBytes substitutes U+FFFD for a
    // lone surrogate rather than refusing it, and the substitution is lossy. A row whose
    // Property, SortValue or Key held one produced a token that decoded to a *different*
    // cursor, so the reader landed on the wrong window and nothing reported it. Refusing
    // reaches the empty-string path Encode already has for an over-long part: no link is
    // a cost a reader can see, a wrong window is not.
    private static bool TryWrite(Span<byte> destination, ref int at, string? text, int byteCount)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination[at..], text is null ? NullMarker : (ushort)byteCount);
        at += 2;

        if (text is null)
        {
            return true;
        }

        // byteCount came from GetByteCount, which counts the replacement it would have
        // written, so a short write is the same defect as a failed one.
        if (Utf8.FromUtf16(text, destination.Slice(at, byteCount), out _, out int written, false) != OperationStatus.Done
            || written != byteCount)
        {
            return false;
        }

        at += byteCount;
        return true;
    }

    private static bool TryRead(ReadOnlySpan<byte> body, ref int at, int cap, out string? text)
    {
        text = null;

        if (at + 2 > body.Length)
        {
            return false;
        }

        int length = BinaryPrimitives.ReadUInt16BigEndian(body[at..]);
        at += 2;

        if (length == NullMarker)
        {
            return true;
        }

        if (length > cap || at + length > body.Length)
        {
            return false;
        }

        // Strict, because Encoding.UTF8.GetString substitutes U+FFFD for a malformed
        // sequence rather than refusing it. A tampered token then decodes to a cursor
        // this release did not write, which is what TryDecode returning true is meant
        // to rule out -- and the substitution is lossy, so the token no longer
        // round-trips. Found by the property test in spec 018, not by an example.
        // MaxValueBytes, not cap, and that is safe only while every caller's cap is at or
        // below it: UTF-8 never expands past one char per byte, so a cap of 512 bytes
        // cannot fill more than 512 chars. A caller passing a larger cap would get
        // DestinationTooSmall reported below as malformed input, which is the wrong
        // answer -- both call sites are in TryDecode above, and both stay within it.
        Span<char> chars = stackalloc char[MaxValueBytes];
        if (Utf8.ToUtf16(body.Slice(at, length), chars, out _, out int written, false) != OperationStatus.Done)
        {
            return false;
        }

        text = new string(chars[..written]);
        at += length;
        return true;
    }
}
