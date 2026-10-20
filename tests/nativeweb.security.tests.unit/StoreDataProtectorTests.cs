using System.Security.Cryptography;

using NativeWeb.Security;

using Xunit;

namespace NativeWeb.Security.Tests.Unit;

/// <summary>
/// Authenticated encryption for the BFF durable store's data column. The
/// tamper tests are the point of AES-GCM here: a store value that decrypts
/// after modification would mean the authentication tag is not being checked.
/// </summary>
public sealed class StoreDataProtectorTests
{
    // FIPS 180-4 appendix B vectors, the non-circular anchors for the
    // passphrase branch of DeriveKey.
    private const string AbcDigest = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string TwoBlockDigest = "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1";

    private readonly StoreDataProtector _protector = new(SequentialKey());

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void Ctor_KeyNot32Bytes_ThrowsNamingKey(int length)
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new StoreDataProtector(KeyOfLength(length)));

        Assert.Equal("key", ex.ParamName);
    }

    [Fact]
    public void Ctor_KeyOfExactly32Bytes_IsAccepted()
    {
        _ = new StoreDataProtector(SequentialKey());
    }

    [Fact]
    public void DeriveKey_Base64SecretDecodingTo32Bytes_IsUsedVerbatim()
    {
        string secret = Convert.ToBase64String(SequentialKey());

        Assert.Equal(44, secret.Length);
        Assert.Equal(SequentialKey(), StoreDataProtector.DeriveKey(secret));
    }

    [Fact]
    public void DeriveKey_HexSecretOf64Characters_IsUsedVerbatim()
    {
        string secret = Convert.ToHexString(SequentialKey());

        Assert.Equal(64, secret.Length);
        Assert.Equal(SequentialKey(), StoreDataProtector.DeriveKey(secret));
    }

    [Theory]
    [InlineData("abc", AbcDigest)]
    [InlineData("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", TwoBlockDigest)]
    public void DeriveKey_Passphrase_IsSha256OfUtf8Bytes(string secret, string expectedHex)
    {
        Assert.Equal(Convert.FromHexString(expectedHex), StoreDataProtector.DeriveKey(secret));
    }

    [Fact]
    public void DeriveKey_Base64DecodingToWrongLength_IsHashedNotUsedVerbatim()
    {
        string secret = Convert.ToBase64String(new byte[16]);

        Assert.Equal(StoreDataProtector.KeyBytes, StoreDataProtector.DeriveKey(secret).Length);
    }

    [Fact]
    public void DeriveKey_HexOfWrongLength_IsHashedNotUsedVerbatim()
    {
        string secret = Convert.ToHexString(new byte[16]);

        Assert.Equal(32, secret.Length);
        Assert.Equal(StoreDataProtector.KeyBytes, StoreDataProtector.DeriveKey(secret).Length);
    }

    [Fact]
    public void DeriveKey_SameSecretTwice_ReturnsSameKey()
    {
        Assert.Equal(
            StoreDataProtector.DeriveKey("passphrase-secret"),
            StoreDataProtector.DeriveKey("passphrase-secret"));
    }

    [Fact]
    public void DeriveKey_DifferentSecrets_ReturnDifferentKeys()
    {
        Assert.NotEqual(
            StoreDataProtector.DeriveKey("alpha-secret"),
            StoreDataProtector.DeriveKey("beta-secret"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void DeriveKey_NullOrWhiteSpace_ThrowsArgumentException(string? secret)
    {
        Assert.ThrowsAny<ArgumentException>(() => StoreDataProtector.DeriveKey(secret!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("store-value-123")]
    [InlineData("🎬 안녕 你好 naïve 日本語")]
    [InlineData("enc:v1:embedded")]
    [InlineData("{\"sub\":\"user-7\",\"scope\":\"bff session\",\"exp\":1893456000}")]
    public void Unprotect_ProtectedValue_RoundTrips(string plaintext)
    {
        Assert.Equal(plaintext, _protector.Unprotect(_protector.Protect(plaintext)));
    }

    [Fact]
    public void Unprotect_ProtectedLongValue_RoundTrips()
    {
        string plaintext = new('x', 10_000);

        Assert.Equal(plaintext, _protector.Unprotect(_protector.Protect(plaintext)));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("abc", 3)]
    [InlineData("é", 2)]
    [InlineData("你好", 6)]
    public void Protect_AnyPlaintext_WritesNonceCipherTagEnvelope(string plaintext, int plainByteCount)
    {
        string stored = _protector.Protect(plaintext);

        Assert.StartsWith(StoreDataProtector.Prefix, stored, StringComparison.Ordinal);
        byte[] envelope = Convert.FromBase64String(stored[StoreDataProtector.Prefix.Length..]);
        Assert.Equal(12 + plainByteCount + 16, envelope.Length);
    }

    [Fact]
    public void Protect_SamePlaintextTwice_ProducesDifferentCiphertext()
    {
        const string plaintext = "attack at dawn";
        string first = _protector.Protect(plaintext);
        string second = _protector.Protect(plaintext);

        Assert.NotEqual(first, second);
        Assert.Equal(plaintext, _protector.Unprotect(first));
        Assert.Equal(plaintext, _protector.Unprotect(second));
    }

    [Fact]
    public void Protect_NullPlaintext_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _protector.Protect(null!));
    }

    [Fact]
    public void Unprotect_NullStored_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _protector.Unprotect(null!));
    }

    [Fact]
    public void Unprotect_FlippedCipherBit_ThrowsCryptographicException()
    {
        byte[] envelope = EnvelopeOf("attack at dawn");
        envelope[12] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(ToStored(envelope)));
    }

    [Fact]
    public void Unprotect_FlippedTagBit_ThrowsCryptographicException()
    {
        byte[] envelope = EnvelopeOf("attack at dawn");
        envelope[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(ToStored(envelope)));
    }

    [Fact]
    public void Unprotect_FlippedNonceBit_ThrowsCryptographicException()
    {
        byte[] envelope = EnvelopeOf("attack at dawn");
        envelope[0] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(ToStored(envelope)));
    }

    [Fact]
    public void Unprotect_TruncatedEnvelope_ThrowsCryptographicException()
    {
        byte[] envelope = EnvelopeOf("attack at dawn");

        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(ToStored(envelope[..^1])));
    }

    [Fact]
    public void Unprotect_AppendedByte_ThrowsCryptographicException()
    {
        byte[] envelope = EnvelopeOf("attack at dawn");
        byte[] mutated = new byte[envelope.Length + 1];
        envelope.CopyTo(mutated, 0);

        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(ToStored(mutated)));
    }

    [Fact]
    public void TryUnprotect_ProtectedValue_RoundTripsAndReportsTrue()
    {
        Assert.True(_protector.TryUnprotect(_protector.Protect("attack at dawn"), out string? plain));
        Assert.Equal("attack at dawn", plain);
    }

    [Fact]
    public void TryUnprotect_ValueFromAnotherKey_IsFalseAndYieldsNothing()
    {
        string stored = new StoreDataProtector(RandomNumberGenerator.GetBytes(StoreDataProtector.KeyBytes))
            .Protect("attack at dawn");

        Assert.False(_protector.TryUnprotect(stored, out string? plain));
        Assert.Null(plain);
    }

    [Fact]
    public void TryUnprotect_FlippedTagBit_IsFalseRatherThanThrowing()
    {
        byte[] envelope = EnvelopeOf("attack at dawn");
        envelope[^1] ^= 0x01;

        Assert.False(_protector.TryUnprotect(ToStored(envelope), out string? plain));
        Assert.Null(plain);
    }

    [Fact]
    public void TryUnprotect_UnprefixedValue_IsFalseRatherThanThrowing()
    {
        Assert.False(_protector.TryUnprotect("attack at dawn", out string? plain));
        Assert.Null(plain);
    }

    [Fact]
    public void TryUnprotect_NonBase64AfterPrefix_IsFalseRatherThanThrowing()
    {
        // FormatException, not CryptographicException: the same unreadable value by another route.
        Assert.False(_protector.TryUnprotect(StoreDataProtector.Prefix + "@@@@", out string? plain));
        Assert.Null(plain);
    }

    [Fact]
    public void TryUnprotect_NullStored_ThrowsArgumentNullException()
    {
        // Not a value that failed to decrypt. A null argument is the caller's bug and stays one.
        Assert.Throws<ArgumentNullException>(() => _protector.TryUnprotect(null!, out _));
    }

    [Fact]
    public void Unprotect_NonBase64AfterPrefix_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(
            () => _protector.Unprotect(StoreDataProtector.Prefix + "@@@@"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(27)]
    public void Unprotect_EnvelopeShorterThan28Bytes_ThrowsCryptographicException(int length)
    {
        string stored = ToStored(new byte[length]);

        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(stored));
    }

    [Fact]
    public void Unprotect_CiphertextFromDifferentKey_ThrowsCryptographicException()
    {
        byte[] otherKey = new byte[StoreDataProtector.KeyBytes];
        Array.Fill(otherKey, (byte)0xC9);
        var other = new StoreDataProtector(otherKey);
        string stored = other.Protect("attack at dawn");

        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(stored));
    }

    [Theory]
    [InlineData("")]
    [InlineData("legacy plaintext row")]
    [InlineData("enc:v2:AAAA")]
    [InlineData("ENC:V1:AAAA")]
    public void Unprotect_NoV1Prefix_ThrowsAndNamesTheOptIn(string stored)
    {
        CryptographicException ex =
            Assert.Throws<CryptographicException>(() => _protector.Unprotect(stored));

        Assert.Contains(StoreDataProtector.Prefix, ex.Message, StringComparison.Ordinal);
        Assert.Contains("allowLegacyPlaintext", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("legacy plaintext row")]
    [InlineData("enc:v2:AAAA")]
    [InlineData("ENC:V1:AAAA")]
    public void Unprotect_NoV1Prefix_WithLegacyOptIn_ReturnsValueUnchanged(string stored)
    {
        var legacy = new StoreDataProtector(SequentialKey(), allowLegacyPlaintext: true);

        Assert.Equal(stored, legacy.Unprotect(stored));
    }

    [Fact]
    public void Unprotect_LegacyOptIn_StillRejectsATamperedEnvelope()
    {
        var legacy = new StoreDataProtector(SequentialKey(), allowLegacyPlaintext: true);
        byte[] envelope = EnvelopeOf("attack at dawn");
        envelope[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => legacy.Unprotect(ToStored(envelope)));
    }

    private byte[] EnvelopeOf(string plaintext)
        => Convert.FromBase64String(_protector.Protect(plaintext)[StoreDataProtector.Prefix.Length..]);

    private static string ToStored(byte[] envelope)
        => StoreDataProtector.Prefix + Convert.ToBase64String(envelope);

    private static byte[] SequentialKey()
        => KeyOfLength(StoreDataProtector.KeyBytes);

    private static byte[] KeyOfLength(int length)
    {
        byte[] key = new byte[length];
        for (int i = 0; i < length; i++)
        {
            key[i] = (byte)i;
        }
        return key;
    }
}
