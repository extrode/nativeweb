using NativeWeb.Security;

using Xunit;

namespace NativeWeb.Security.Tests.Unit;

/// <summary>
/// Argon2id passphrase hashing. Every Hash and Verify call spends the full
/// OWASP memory and iteration budget, so the facts here keep the call count low.
/// </summary>
public sealed class PassphraseTests
{
    [Fact]
    public void Hash_AnyPassphrase_ReturnsArgon2idPhcStringFittingTheColumn()
    {
        string phc = Passphrases.Hash("correct-horse-battery-staple");

        Assert.StartsWith("$argon2id$", phc);
        Assert.InRange(phc.Length, "$argon2id$".Length + 1, 255);
        Assert.False(phc.Contains('\0'));
        Assert.Contains("m=19456", phc);
        Assert.Contains("t=2", phc);
    }

    [Fact]
    public void Hash_SamePassphraseTwice_ProducesDifferentHashes()
    {
        string first = Passphrases.Hash("same-passphrase");
        string second = Passphrases.Hash("same-passphrase");

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Hash_NullOrEmptyPassphrase_Throws(string? passphrase)
    {
        Assert.ThrowsAny<ArgumentException>(() => Passphrases.Hash(passphrase!));
    }

    [Fact]
    public void Verify_PassphraseMatchingTheHash_ReturnsTrue()
    {
        string phc = Passphrases.Hash("matching-passphrase");

        Assert.True(Passphrases.Verify(phc, "matching-passphrase"));
    }

    [Fact]
    public void Verify_DifferentPassphrase_ReturnsFalse()
    {
        string phc = Passphrases.Hash("original-passphrase");

        Assert.False(Passphrases.Verify(phc, "other-passphrase"));
    }

    [Fact]
    public void Verify_PassphraseWithTrailingSpace_ReturnsFalse()
    {
        string phc = Passphrases.Hash("space-sensitive");

        Assert.False(Passphrases.Verify(phc, "space-sensitive "));
    }

    [Theory]
    [InlineData("not-a-phc-string-at-all")]
    [InlineData("$argon2id$")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$")]
    [InlineData("$argon2i$v=19$m=19456,t=2,p=1$c29tZXNhbHRzb21lc2FsdA$RdescudvJCsgt3ub+b+dWRWJTmaaJObG")]
    public void Verify_MalformedPhcString_ReturnsFalseWithoutThrowing(string phcHash)
    {
        Assert.False(Passphrases.Verify(phcHash, "any-passphrase"));
    }

    [Fact]
    public void Verify_PhcStringWithAlteredTail_ReturnsFalse()
    {
        string phc = Passphrases.Hash("tamper-target");
        char swapped = phc[^1] == 'A' ? 'B' : 'A';
        string tampered = phc[..^1] + swapped;

        Assert.False(Passphrases.Verify(tampered, "tamper-target"));
    }

    [Theory]
    [InlineData("pässwörd-🔐-日本語")]
    [InlineData("Ψεύτικος-κωδικός-πρόσβασης")]
    public void Verify_UnicodePassphrase_RoundTrips(string passphrase)
    {
        string phc = Passphrases.Hash(passphrase);

        Assert.True(Passphrases.Verify(phc, passphrase));
    }

    [Fact]
    public void Verify_LongPassphrase_RoundTrips()
    {
        string passphrase = new('x', 1024);
        string phc = Passphrases.Hash(passphrase);

        Assert.True(Passphrases.Verify(phc, passphrase));
    }

    [Theory]
    [InlineData(null, "passphrase")]
    [InlineData("", "passphrase")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$x$y", null)]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$x$y", "")]
    public void Verify_NullOrEmptyArgument_Throws(string? phcHash, string? passphrase)
    {
        Assert.ThrowsAny<ArgumentException>(() => Passphrases.Verify(phcHash!, passphrase!));
    }

    [Fact]
    public void NeedsRehash_HashFromCurrentParameters_ReturnsFalse()
    {
        string phc = Passphrases.Hash("current-parameters");

        Assert.False(Passphrases.NeedsRehash(phc));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NeedsRehash_NullOrEmptyHash_Throws(string? phcHash)
    {
        Assert.ThrowsAny<ArgumentException>(() => Passphrases.NeedsRehash(phcHash!));
    }
}
