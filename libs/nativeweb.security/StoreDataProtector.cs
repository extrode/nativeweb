using System.Security.Cryptography;
using System.Text;

namespace NativeWeb.Security;

// Authenticated encryption for the BFF durable store's `data` column. The store
// holds material that MUST round-trip in plaintext to the server (live access/
// refresh tokens for opaque sessions, and the in-flight registration passphrase),
// so hashing is not an option — the correct at-rest protection is encryption with
// a key held outside the database. AES-256-GCM keyed by a 32-byte app secret;
// ciphertext is `enc:v1:` + base64(nonce(12) || ciphertext || tag(16)).
public sealed class StoreDataProtector
{
    public const string Prefix = "enc:v1:";
    public const int KeyBytes = 32;

    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _key;

    public StoreDataProtector(byte[] key)
    {
        if (key.Length != KeyBytes)
        {
            throw new ArgumentException($"Store encryption key must be {KeyBytes} bytes.", nameof(key));
        }
        _key = key;
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

    // Reverses Protect. A value without the versioned prefix is legacy plaintext
    // (written before encryption was introduced) and is returned unchanged so
    // pre-existing rows keep working until they age out via their TTL.
    public string Unprotect(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return stored;
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
