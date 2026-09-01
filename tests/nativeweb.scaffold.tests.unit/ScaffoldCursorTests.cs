using System.Buffers.Binary;
using System.Buffers.Text;
using System.Text;

using NativeWeb.Scaffold;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Unit;

/// <summary>
/// The opaque list cursor. Everything here is either a round trip or a rejection,
/// because the argument to <see cref="ScaffoldCursor.TryDecode"/> is whatever was in
/// the query string and the contract is that no shape of it throws.
/// </summary>
public sealed class ScaffoldCursorTests
{
    private static ScaffoldCursor Decode(string token)
    {
        Assert.True(ScaffoldCursor.TryDecode(token, out ScaffoldCursor cursor));
        return cursor;
    }

    // version, flags, then each of the three strings as a big-endian ushort length
    // followed by its UTF-8 bytes; 0xFFFF as the length means null.
    private static string Encoded(byte version, byte flags, params string?[] parts)
    {
        var bytes = new List<byte> { version, flags };
        byte[] length = new byte[2];

        foreach (string? part in parts)
        {
            if (part is null)
            {
                BinaryPrimitives.WriteUInt16BigEndian(length, ushort.MaxValue);
                bytes.AddRange(length);
                continue;
            }

            byte[] text = Encoding.UTF8.GetBytes(part);
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)text.Length);
            bytes.AddRange(length);
            bytes.AddRange(text);
        }

        return Base64Url.EncodeToString(bytes.ToArray());
    }

    [Theory]
    [InlineData(ScaffoldSortDirection.Ascending, false)]
    [InlineData(ScaffoldSortDirection.Ascending, true)]
    [InlineData(ScaffoldSortDirection.Descending, false)]
    [InlineData(ScaffoldSortDirection.Descending, true)]
    public void Encode_ThenDecode_ReturnsEveryField(ScaffoldSortDirection direction, bool backward)
    {
        var original = new ScaffoldCursor("Title", direction, "Meditations", "a1", backward);

        Assert.Equal(original, Decode(original.Encode()));
    }

    [Fact]
    public void ANullSortValue_DoesNotDecodeAsAnEmptyOne()
    {
        string nullValue = new ScaffoldCursor("Author", ScaffoldSortDirection.Ascending, null, "a1", false).Encode();
        string emptyValue = new ScaffoldCursor("Author", ScaffoldSortDirection.Ascending, "", "a1", false).Encode();

        Assert.NotEqual(nullValue, emptyValue);
        Assert.Null(Decode(nullValue).SortValue);
        Assert.Equal("", Decode(emptyValue).SortValue);
    }

    [Fact]
    public void MultiByteText_SurvivesTheRoundTrip()
    {
        var original = new ScaffoldCursor("Title", ScaffoldSortDirection.Ascending, "Ünicode — 日本語", "ключ", false);

        Assert.Equal(original, Decode(original.Encode()));
    }

    [Fact]
    public void AnEncodedCursor_IsUrlSafe() =>
        Assert.All(
            new ScaffoldCursor("Title", ScaffoldSortDirection.Descending, "a b/c+d?e", "a1", true).Encode(),
            c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_', $"'{c}' needs escaping in a URL"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!!!!")]
    [InlineData("not a cursor")]
    [InlineData("a")]
    public void Garbage_IsRejected(string? raw) =>
        Assert.False(ScaffoldCursor.TryDecode(raw, out _));

    [Fact]
    public void ATruncatedCursor_IsRejected()
    {
        string token = new ScaffoldCursor("Title", ScaffoldSortDirection.Ascending, "Meditations", "a1", false).Encode();

        for (int cut = 1; cut < token.Length; cut++)
        {
            Assert.False(ScaffoldCursor.TryDecode(token[..cut], out _));
        }
    }

    [Fact]
    public void AnUnknownVersion_IsRejected() =>
        Assert.False(ScaffoldCursor.TryDecode(Encoded(2, 0, "Title", "x", "a1"), out _));

    [Fact]
    public void AnUnknownFlagBit_IsRejected() =>
        Assert.False(ScaffoldCursor.TryDecode(Encoded(1, 4, "Title", "x", "a1"), out _));

    [Fact]
    public void ANullProperty_IsRejected() =>
        Assert.False(ScaffoldCursor.TryDecode(Encoded(1, 0, null, "x", "a1"), out _));

    [Fact]
    public void ANullKey_IsRejected() =>
        Assert.False(ScaffoldCursor.TryDecode(Encoded(1, 0, "Title", "x", null), out _));

    [Fact]
    public void ALengthPrefixPastTheEnd_IsRejected()
    {
        byte[] bytes = [1, 0, 0x00, 0x40, (byte)'T', (byte)'i'];

        Assert.False(ScaffoldCursor.TryDecode(Base64Url.EncodeToString(bytes), out _));
    }

    [Fact]
    public void TrailingBytesAfterTheThirdValue_AreRejected()
    {
        string token = Encoded(1, 0, "Title", "x", "a1");
        byte[] bytes = [.. Base64Url.DecodeFromChars(token.AsSpan()), 0];

        Assert.False(ScaffoldCursor.TryDecode(Base64Url.EncodeToString(bytes), out _));
    }

    [Fact]
    public void AnOverLongToken_IsRejectedWithoutDecoding() =>
        Assert.False(ScaffoldCursor.TryDecode(new string('A', 4096), out _));

    [Theory]
    [InlineData(129, 1, 1)]
    [InlineData(1, 513, 1)]
    [InlineData(1, 1, 513)]
    public void APartTooLongToEncode_YieldsNoToken(int property, int value, int key) =>
        Assert.Equal(
            "",
            new ScaffoldCursor(
                new string('p', property),
                ScaffoldSortDirection.Ascending,
                new string('v', value),
                new string('k', key),
                false).Encode());

    [Fact]
    public void APartAtTheLimit_StillEncodes()
    {
        var original = new ScaffoldCursor(
            new string('p', 128),
            ScaffoldSortDirection.Descending,
            new string('v', 512),
            new string('k', 512),
            true);

        Assert.Equal(original, Decode(original.Encode()));
    }

    [Theory]
    [InlineData("Title", ScaffoldSortDirection.Ascending, true)]
    [InlineData("Title", ScaffoldSortDirection.Descending, false)]
    [InlineData("Author", ScaffoldSortDirection.Ascending, false)]
    [InlineData("title", ScaffoldSortDirection.Ascending, false)]
    public void Matches_IsTrueOnlyForTheOrderingItWasIssuedFor(
        string property,
        ScaffoldSortDirection direction,
        bool expected) =>
        Assert.Equal(
            expected,
            new ScaffoldCursor("Title", ScaffoldSortDirection.Ascending, "x", "a1", false)
                .Matches(new ScaffoldSortSpec(property, direction)));
}
