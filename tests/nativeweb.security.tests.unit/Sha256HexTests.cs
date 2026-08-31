using System.Security.Cryptography;
using System.Text;

using NativeWeb.Security;

using Xunit;

namespace NativeWeb.Security.Tests.Unit;

/// <summary>
/// The repository's single at-rest hash routine. Its stored form is a
/// <c>CHAR(64)</c> column in every consumer, so the encoding is a schema
/// contract and not an implementation detail.
/// </summary>
public sealed class Sha256HexTests
{
    // FIPS 180-4 appendix B and the SHA-256 empty-string digest. These two are the
    // only non-circular anchors here: every other fact below compares one code path
    // against another, which cannot catch the algorithm being wrong.
    private const string EmptyDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string AbcDigest = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Theory]
    [InlineData("", EmptyDigest)]
    [InlineData("abc", AbcDigest)]
    public void OfUtf8_KnownVector_MatchesFips180_4(string value, string expected)
    {
        Assert.Equal(expected, Sha256Hex.OfUtf8(value));
    }

    [Fact]
    public void OfUtf8_AnyInput_Is64LowercaseHexCharacters()
    {
        string digest = Sha256Hex.OfUtf8("token-value");

        Assert.Equal(64, digest.Length);
        Assert.Equal(digest.ToLowerInvariant(), digest);
        Assert.All(digest, c => Assert.Contains(c, "0123456789abcdef"));
    }

    [Fact]
    public void OfUtf8_NonAscii_HashesTheUtf8BytesNotTheUtf16Ones()
    {
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(new byte[] { 0xC3, 0xA9 })),
            Sha256Hex.OfUtf8("é"));
    }

    // OfUtf8 stackallocs at or below 256 UTF-8 bytes and heap-allocates above it.
    // A wrong bound truncates the input silently, so both branches are driven and
    // checked against the same input encoded the same way.
    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(4096)]
    public void OfUtf8_AcrossTheStackallocBoundary_HashesEveryByte(int length)
    {
        string value = new('a', length);

        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value))),
            Sha256Hex.OfUtf8(value));
    }

    [Fact]
    public void OfUtf8_MultiByteCharsPastTheBoundary_CountsBytesNotChars()
    {
        string value = new('é', 200);

        Assert.Equal(400, Encoding.UTF8.GetByteCount(value));
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value))),
            Sha256Hex.OfUtf8(value));
    }

    [Fact]
    public void OfUtf8_OneBitApart_ProducesUnrelatedDigests()
    {
        Assert.NotEqual(Sha256Hex.OfUtf8("token-a"), Sha256Hex.OfUtf8("token-b"));
    }
}
