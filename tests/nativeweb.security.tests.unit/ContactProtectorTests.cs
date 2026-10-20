using System.Security.Cryptography;
using System.Text;

using NativeWeb.Security;

using Xunit;

namespace NativeWeb.Security.Tests.Unit;

/// <summary>
/// At-rest protection for contact PII. The file's central claims are key separation
/// (independent encryption, index, and fake-mask subkeys of one master) and a
/// scope-bound blind index that cannot link one person across tenants without the secret.
/// </summary>
public sealed class ContactProtectorTests
{
    private static ContactProtector Protector(string secret) =>
        new(ContactProtector.DeriveKey(secret));

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Constructor_MasterKeyNot32Bytes_Throws(int keyLength)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ContactProtector(new byte[keyLength]));
    }

    [Fact]
    public void DeriveKey_ConfiguredSecret_Produces32DeterministicBytes()
    {
        string master = Convert.ToHexString(ContactProtector.DeriveKey("configured contact secret"));

        Assert.Equal(64, master.Length);
        Assert.Equal(master, Convert.ToHexString(ContactProtector.DeriveKey("configured contact secret")));
    }

    [Fact]
    public void DeriveKey_DifferentSecrets_ProduceDifferentMasterKeys()
    {
        Assert.NotEqual(
            Convert.ToHexString(ContactProtector.DeriveKey("secret-a")),
            Convert.ToHexString(ContactProtector.DeriveKey("secret-b")));
    }

    [Fact]
    public void Unprotect_ProtectedValue_RoundTripsToThePlaintext()
    {
        ContactProtector protector = Protector("secret-a");

        string ciphertext = protector.Protect("alice@example.test");

        Assert.DoesNotContain("alice@example.test", ciphertext);
        Assert.Equal("alice@example.test", protector.Unprotect(ciphertext));
    }

    [Fact]
    public void Protect_SamePlaintextTwice_ProducesDifferentCiphertext()
    {
        ContactProtector protector = Protector("secret-a");

        Assert.NotEqual(
            protector.Protect("alice@example.test"),
            protector.Protect("alice@example.test"));
    }

    // ContactProtector builds its inner StoreDataProtector itself and does not offer the
    // legacy opt-in, so a contact column that was never encrypted is refused rather than
    // read back as if it had been.
    [Fact]
    public void Unprotect_ValueWithoutVersionedPrefix_Throws()
    {
        Assert.ThrowsAny<CryptographicException>(
            () => Protector("secret-a").Unprotect("legacy@example.test"));
    }

    [Fact]
    public void Protect_UnderTheEncSubkey_IsNotDecryptableByAStoreProtectorOnTheSameMaster()
    {
        byte[] master = ContactProtector.DeriveKey("secret-a");

        string ciphertext = new ContactProtector(master).Protect("alice@example.test");

        Assert.ThrowsAny<CryptographicException>(() => new StoreDataProtector(master).Unprotect(ciphertext));
    }

    [Fact]
    public void EmailIndex_AnyAddressAndScope_Is64LowercaseHexCharacters()
    {
        string index = Protector("secret-a").EmailIndex(1, "alice@example.test");

        Assert.Equal(64, index.Length);
        Assert.Equal(index.ToLowerInvariant(), index);
        Assert.All(index, c => Assert.Contains(c, "0123456789abcdef"));
    }

    [Fact]
    public void EmailIndex_MixedCaseAndWhitespace_NormalizesLikeTheOldCitextColumn()
    {
        ContactProtector protector = Protector("secret-a");

        Assert.Equal(
            protector.EmailIndex(1, "alice@example.test"),
            protector.EmailIndex(1, "  Alice@Example.TEST "));
    }

    [Fact]
    public void EmailIndex_DifferentAddresses_ProduceDifferentIndexes()
    {
        ContactProtector protector = Protector("secret-a");

        Assert.NotEqual(
            protector.EmailIndex(1, "alice@example.test"),
            protector.EmailIndex(1, "bob@example.test"));
    }

    
    // Each pair differs only in the least significant scope byte: the byte an
    // off-by-one in the big-endian write would let the value overwrite, which would
    // make the two scopes silently share one index.
    [Theory]
    [InlineData(0L, 1L)]
    [InlineData(-5L, -4L)]
    [InlineData(long.MinValue, long.MinValue + 1L)]
    [InlineData(long.MaxValue, long.MaxValue - 1L)]
    public void EmailIndex_DifferentScopeIds_ProduceDifferentIndexes(long scopeA, long scopeB)
    {
        ContactProtector protector = Protector("secret-a");

        Assert.NotEqual(
            protector.EmailIndex(scopeA, "alice@example.test"),
            protector.EmailIndex(scopeB, "alice@example.test"));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void EmailIndex_SameScopeId_ReproducesTheIndex(long scopeId)
    {
        ContactProtector protector = Protector("secret-a");

        Assert.Equal(
            protector.EmailIndex(scopeId, "alice@example.test"),
            protector.EmailIndex(scopeId, "alice@example.test"));
    }

    [Fact]
    public void PhoneIndex_SameNumberAndScope_TrimsAndIsDeterministic()
    {
        ContactProtector protector = Protector("secret-a");
        string index = protector.PhoneIndex(1, "+15551234567");

        Assert.Equal(index, protector.PhoneIndex(1, " +15551234567 "));
        Assert.NotEqual(index, protector.PhoneIndex(1, "+15551234568"));
    }

    [Fact]
    public void PhoneIndex_MixedCaseValue_KeepsCaseWhereEmailIndexNormalizesIt()
    {
        ContactProtector protector = Protector("secret-a");
        string mixed = "Alice@Example.test";

        Assert.Equal(protector.EmailIndex(1, mixed), protector.EmailIndex(1, mixed.ToLowerInvariant()));
        Assert.NotEqual(protector.PhoneIndex(1, mixed), protector.PhoneIndex(1, mixed.ToLowerInvariant()));
    }

    [Fact]
    public void PhoneIndex_DifferentScopeIds_ProduceDifferentIndexes()
    {
        ContactProtector protector = Protector("secret-a");

        Assert.NotEqual(
            protector.PhoneIndex(1, "+15551234567"),
            protector.PhoneIndex(2, "+15551234567"));
    }

    [Fact]
    public void Indexes_DifferentMasterKeys_ProduceDifferentIndexesForTheSameValue()
    {
        ContactProtector a = Protector("secret-a");
        ContactProtector b = Protector("secret-b");

        Assert.NotEqual(a.EmailIndex(1, "alice@example.test"), b.EmailIndex(1, "alice@example.test"));
        Assert.NotEqual(a.PhoneIndex(1, "+15551234567"), b.PhoneIndex(1, "+15551234567"));
    }

    
    
    }
