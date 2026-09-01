using System.Buffers.Text;
using System.Text;

using CsCheck;

using NativeWeb.Scaffold;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Unit;

/// <summary>
/// The cursor codec held to properties rather than examples. Spec 017 shipped two
/// defects here that example tests did not reach -- <c>Base64Url.TryDecodeFromChars</c>
/// throwing on a character outside the alphabet, and a key written in display form --
/// and a mutation run then left four more mutants alive in <c>TryDecode</c>'s
/// truncated-prefix and overrun-length branches. Those branches are reachable by
/// mangling a token, which is what a generator does and a hand-written case does not.
/// </summary>
public sealed class ScaffoldCursorPropertyTests
{
    private const long Iterations = 2_000;

    private static readonly Gen<string> Text =
        Gen.Char["abcXYZ019 -_.:+/=äéß中"].Array[0, 24].Select(c => new string(c));

    private static readonly Gen<ScaffoldCursor> Cursors = Gen.Select(
        Gen.Char["abcXYZ019_"].Array[1, 20].Select(c => new string(c)),
        Gen.Enum<ScaffoldSortDirection>(),
        Gen.OneOf(Gen.Const((string?)null), Text.Select(s => (string?)s)),
        Text,
        Gen.Bool,
        (property, direction, sortValue, key, backward) =>
            new ScaffoldCursor(property, direction, sortValue, key, backward));

    private static string Mangle(string token, int at, char with)
    {
        char[] chars = token.ToCharArray();
        chars[at % chars.Length] = with;
        return new string(chars);
    }

    [Fact]
    public void ACursorSurvivesTheRoundTrip() =>
        Cursors.Sample(
            cursor =>
            {
                string token = cursor.Encode();

                Assert.NotEqual(string.Empty, token);
                Assert.True(ScaffoldCursor.TryDecode(token, out ScaffoldCursor read));
                Assert.Equal(cursor, read);
            },
            iter: Iterations);

    [Fact]
    public void ANullSortValueStaysDistinctFromTheEmptyString() =>
        Gen.Select(Text, Text, Gen.Bool).Sample(
            (property, key, backward) =>
            {
                ScaffoldCursor.TryDecode(
                    new ScaffoldCursor($"p{property}", ScaffoldSortDirection.Ascending, null, key, backward).Encode(),
                    out ScaffoldCursor asNull);
                ScaffoldCursor.TryDecode(
                    new ScaffoldCursor($"p{property}", ScaffoldSortDirection.Ascending, "", key, backward).Encode(),
                    out ScaffoldCursor asEmpty);

                Assert.Null(asNull.SortValue);
                Assert.Equal("", asEmpty.SortValue);
            },
            iter: Iterations);

    [Fact]
    public void EncodingIsStable() =>
        Cursors.Sample(cursor => Assert.Equal(cursor.Encode(), cursor.Encode()), iter: Iterations);

    [Fact]
    public void TwoCursorsDifferingInAnyPartEncodeDifferently() =>
        Gen.Select(Cursors, Cursors).Where(t => t.Item1 != t.Item2).Sample(
            (first, second) => Assert.NotEqual(first.Encode(), second.Encode()),
            iter: Iterations);

    [Fact]
    public void AnArbitraryStringIsRejectedRatherThanThrowing() =>
        Gen.String[0, 64].Sample(
            value => Assert.False(
                ScaffoldCursor.TryDecode(value, out ScaffoldCursor read) && read.Property.Length == 0),
            iter: Iterations);

    [Fact]
    public void ArbitraryBytesUnderAValidBase64UrlEncodingAreRejectedRatherThanThrowing() =>
        Gen.Byte.Array[0, 200].Sample(
            bytes =>
            {
                string token = Base64Url.EncodeToString(bytes);

                if (ScaffoldCursor.TryDecode(token, out ScaffoldCursor read))
                {
                    Assert.Equal(token, read.Encode());
                }
            },
            iter: Iterations);

    [Fact]
    public void ATruncatedTokenIsRejectedRatherThanThrowing() =>
        Gen.Select(Cursors, Gen.Int[0, 200]).Sample(
            (cursor, cut) =>
            {
                string token = cursor.Encode();
                string truncated = token[..Math.Max(0, token.Length - (cut % (token.Length + 1)))];

                if (ScaffoldCursor.TryDecode(truncated, out ScaffoldCursor read))
                {
                    Assert.Equal(truncated, read.Encode());
                }
            },
            iter: Iterations);

    [Fact]
    public void ATamperedTokenIsRejectedRatherThanThrowing() =>
        Gen.Select(Cursors, Gen.Int[0, 4096], Gen.Char).Sample(
            (cursor, at, with) =>
            {
                string tampered = Mangle(cursor.Encode(), at, with);

                if (ScaffoldCursor.TryDecode(tampered, out ScaffoldCursor read))
                {
                    Assert.Equal(tampered, read.Encode());
                }
            },
            iter: Iterations);

    [Fact]
    public void AnOversizedPartIsRefusedRatherThanEncodedShort() =>
        Gen.Select(Gen.Int[513, 900], Gen.Int[0, 2]).Sample(
            (length, part) =>
            {
                string big = new('x', length);
                ScaffoldCursor cursor = part switch
                {
                    0 => new ScaffoldCursor(big, ScaffoldSortDirection.Ascending, "v", "k", false),
                    1 => new ScaffoldCursor("p", ScaffoldSortDirection.Ascending, big, "k", false),
                    _ => new ScaffoldCursor("p", ScaffoldSortDirection.Ascending, "v", big, false),
                };

                Assert.Equal(string.Empty, cursor.Encode());
            },
            iter: 500);

    [Fact]
    public void AMultiByteValueIsMeasuredInBytesNotChars()
    {
        var justUnder = new ScaffoldCursor("p", ScaffoldSortDirection.Ascending, new string('中', 170), "k", false);
        var justOver = new ScaffoldCursor("p", ScaffoldSortDirection.Ascending, new string('中', 171), "k", false);

        Assert.NotEqual(string.Empty, justUnder.Encode());
        Assert.Equal(string.Empty, justOver.Encode());
    }

    [Fact]
    public void AnEmptyKeyRoundTrips()
    {
        var cursor = new ScaffoldCursor("Title", ScaffoldSortDirection.Descending, "value", "", true);

        Assert.True(ScaffoldCursor.TryDecode(cursor.Encode(), out ScaffoldCursor read));
        Assert.Equal(cursor, read);
    }

    [Fact]
    public void AVersionByteFromAnotherReleaseIsRefused()
    {
        byte[] payload = Base64Url.DecodeFromChars(
            new ScaffoldCursor("Title", ScaffoldSortDirection.Ascending, "v", "k", false).Encode().AsSpan());
        payload[0] = 2;

        Assert.False(ScaffoldCursor.TryDecode(Base64Url.EncodeToString(payload), out _));
    }

    [Fact]
    public void AnUnknownFlagBitIsRefused()
    {
        byte[] payload = Base64Url.DecodeFromChars(
            new ScaffoldCursor("Title", ScaffoldSortDirection.Ascending, "v", "k", false).Encode().AsSpan());
        payload[1] = 0x04;

        Assert.False(ScaffoldCursor.TryDecode(Base64Url.EncodeToString(payload), out _));
    }

    [Fact]
    public void TrailingBytesAfterTheThirdValueAreRefused()
    {
        string token = new ScaffoldCursor("Title", ScaffoldSortDirection.Ascending, "v", "k", false).Encode();
        byte[] payload = [.. Base64Url.DecodeFromChars(token.AsSpan()), 0x00];

        Assert.False(ScaffoldCursor.TryDecode(Base64Url.EncodeToString(payload), out _));
    }

    [Fact]
    public void ALengthPrefixThatRunsPastTheEndIsRefused()
    {
        string token = new ScaffoldCursor("Title", ScaffoldSortDirection.Ascending, "v", "k", false).Encode();
        byte[] payload = Base64Url.DecodeFromChars(token.AsSpan());
        int propertyLength = Encoding.UTF8.GetByteCount("Title");
        payload[2 + 2 + propertyLength + 1] = 0xFE;

        Assert.False(ScaffoldCursor.TryDecode(Base64Url.EncodeToString(payload), out _));
    }
}
