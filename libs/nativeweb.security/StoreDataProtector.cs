using System.Security.Cryptography;
using System.Text;

namespace NativeWeb.Security;

// Authenticated encryption for a database column holding material that MUST round-trip
// in plaintext to the server -- a live token, a value in flight through a multi-step
// flow -- so hashing is not an option and the correct at-rest protection is encryption
// with a key held outside the database. AES-256-GCM keyed by a 32-byte app secret;
// ciphertext is `enc:v1:` + base64(nonce(12) || ciphertext || tag(16)).
public sealed class StoreDataProtector
{
    public const string Prefix = "enc:v1:";
    public const int KeyBytes = 32;

    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _key;
    private readonly bool _allowLegacyPlaintext;

    /// <param name="key">The 32-byte encryption key.</param>
    /// <param name="allowLegacyPlaintext">
    /// When <see langword="true"/>, <see cref="Unprotect"/> returns a value without the
    /// <see cref="Prefix"/> unchanged instead of throwing. Set this only for a store that
    /// holds rows written before encryption was introduced, and only until they age out.
    /// </param>
    public StoreDataProtector(byte[] key, bool allowLegacyPlaintext = false)
    {
        if (key.Length != KeyBytes)
        {
            throw new ArgumentException($"Store encryption key must be {KeyBytes} bytes.", nameof(key));
        }
        _key = key;
        _allowLegacyPlaintext = allowLegacyPlaintext;
    }

    // Derives a 32-byte key from a configured secret. A base64/hex value of exactly
    // 32 bytes is used verbatim (operator-supplied CSPRNG key); any other string is
    // run through SHA-256 so a passphrase-style value still yields a full-length key.
    public static byte[] DeriveKey(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        if (TryDecode(secret, out byte[] decoded) && decoded.Length == KeyBytes)
        {
            return decoded;
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        Span<byte> nonce = stackalloc byte[NonceBytes];
        RandomNumberGenerator.Fill(nonce);

        byte[] cipher = new byte[plainBytes.Length];
        Span<byte> tag = stackalloc byte[TagBytes];

        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        byte[] envelope = new byte[NonceBytes + cipher.Length + TagBytes];
        nonce.CopyTo(envelope);
        cipher.CopyTo(envelope.AsSpan(NonceBytes));
        tag.CopyTo(envelope.AsSpan(NonceBytes + cipher.Length));

        return Prefix + Convert.ToBase64String(envelope);
    }

    // Reverses Protect. A value without the versioned prefix is refused: in a store this
    // type has always protected, the only ways to reach that branch are a column that was
    // never encrypted and a value that has been replaced, and returning it verbatim
    // reports both as success. The passthrough is still available to a product carrying
    // rows that predate its own encryption rollout, but it is now that product's decision
    // (allowLegacyPlaintext) rather than this library's default. Changed 2026-09-01: the
    // rows the old default existed for left with the identity provider.
    public string Unprotect(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return _allowLegacyPlaintext
                ? stored
                : throw new CryptographicException(
                    $"Stored value does not carry the '{Prefix}' prefix. Construct the protector "
                    + "with allowLegacyPlaintext: true if the store holds rows written before "
                    + "encryption was introduced.");
        }

        byte[] envelope = Convert.FromBase64String(stored[Prefix.Length..]);
        if (envelope.Length < NonceBytes + TagBytes)
        {
            throw new CryptographicException("Store envelope is too short.");
        }

        int cipherLen = envelope.Length - NonceBytes - TagBytes;
        ReadOnlySpan<byte> nonce = envelope.AsSpan(0, NonceBytes);
        ReadOnlySpan<byte> cipher = envelope.AsSpan(NonceBytes, cipherLen);
        ReadOnlySpan<byte> tag = envelope.AsSpan(NonceBytes + cipherLen, TagBytes);

        byte[] plain = new byte[cipherLen];
        using var aes = new AesGcm(_key, TagBytes);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    private static bool TryDecode(string value, out byte[] bytes)
    {
        Span<byte> buffer = stackalloc byte[KeyBytes];
        if (Convert.TryFromBase64String(value, buffer, out int written) && written == KeyBytes)
        {
            bytes = buffer.ToArray();
            return true;
        }

        if (value.Length == KeyBytes * 2)
        {
            try
            {
                bytes = Convert.FromHexString(value);
                return true;
            }
            catch (FormatException)
            {
            }
        }

        bytes = [];
        return false;
    }
}
