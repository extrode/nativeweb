using NativeWeb.Security;

using Xunit;

namespace NativeWeb.Security.Tests.Unit;

/// <summary>
/// Opaque bearer tokens. Generate returns the plaintext the client holds; Hash
/// is the only form ever persisted, so its shape is a schema contract.
/// </summary>
public sealed class OpaqueTokenTests
{
    [Fact]
    public void Generate_AnyCall_Returns43UnpaddedBase64UrlCharacters()
    {
        string token = OpaqueTokens.Generate();

        Assert.Equal(43, token.Length);
        Assert.All(token, c =>
            Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_',
                $"unexpected character '{c}'"));
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void Generate_TwoCalls_ProduceDifferentTokens()
    {
        Assert.NotEqual(OpaqueTokens.Generate(), OpaqueTokens.Generate());
    }

    [Fact]
    public void Generate_BatchOfTwoHundred_HasNoDuplicates()
    {
        var tokens = new HashSet<string>();

        for (int i = 0; i < 200; i++)
        {
            tokens.Add(OpaqueTokens.Generate());
        }

        Assert.Equal(200, tokens.Count);
    }

    [Fact]
    public void Hash_AnyToken_MatchesSha256HexOfTheSameUtf8Bytes()
    {
        string token = OpaqueTokens.Generate();

        Assert.Equal(Sha256Hex.OfUtf8(token), OpaqueTokens.Hash(token));
    }

    [Fact]
    public void Hash_AnyToken_Is64LowercaseHexCharacters()
    {
        string digest = OpaqueTokens.Hash(OpaqueTokens.Generate());

        Assert.Equal(64, digest.Length);
        Assert.Equal(digest.ToLowerInvariant(), digest);
        Assert.All(digest, c => Assert.Contains(c, "0123456789abcdef"));
    }

    [Fact]
    public void Hash_SameTokenTwice_IsStable()
    {
        string token = OpaqueTokens.Generate();

        Assert.Equal(OpaqueTokens.Hash(token), OpaqueTokens.Hash(token));
    }

    [Fact]
    public void Hash_DifferentTokens_ProduceDifferentHashes()
    {
        string first = OpaqueTokens.Generate();
        string second = OpaqueTokens.Generate();

        Assert.NotEqual(OpaqueTokens.Hash(first), OpaqueTokens.Hash(second));
    }

    [Fact]
    public void Hash_AnyToken_DoesNotLeakThePlaintextToken()
    {
        string token = OpaqueTokens.Generate();
        string digest = OpaqueTokens.Hash(token);

        Assert.NotEqual(token, digest);
        Assert.DoesNotContain(token, digest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Hash_NullOrEmptyToken_Throws(string? token)
    {
        Assert.ThrowsAny<ArgumentException>(() => OpaqueTokens.Hash(token!));
    }
}
