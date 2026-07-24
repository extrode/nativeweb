using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Epass.Security;

// At-rest protection for contact PII — the `value` column of email_addresses and
// phone_numbers. Unlike hashed secrets, a contact value must round-trip in plaintext
// (a code/link is sent to it, and it is displayed masked), so it is *encrypted*, not
// hashed. But encryption is non-deterministic (fresh nonce per write), so it cannot
// back an equality lookup or a UNIQUE constraint. Those move to a **blind index**: a
// keyed HMAC of the normalized value, deterministic and non-reversible, stored beside
// the ciphertext and queried instead of the plaintext.
//
// Two independent subkeys are derived from one configured secret (`Epass:ContactKey`)
// so the encryption key and the index key are never the same value:
//   master   = StoreDataProtector.DeriveKey(secret)         (32 bytes)
//   encKey   = SHA-256(master ‖ "enc")   -> AES-256-GCM (via StoreDataProtector)
//   indexKey = SHA-256(master ‖ "index") -> HMAC-SHA-256 blind index
/// <summary>
/// At-rest protection for contact PII (the <c>value</c> column of email_addresses and
/// phone_numbers): AES-256-GCM encryption for plaintext round-trip, plus a realm-scoped
/// HMAC-SHA-256 blind index for equality lookup/uniqueness, since the ciphertext itself
/// cannot back either. Encryption and index keys are independent subkeys of one
/// configured secret (<c>Epass:ContactKey</c>).
/// </summary>
public sealed class ContactProtector
{
    private readonly StoreDataProtector _cipher;
    private readonly byte[] _indexKey;

    /// <summary>Derives the encryption and index subkeys from a 32-byte master key (see <see cref="DeriveKey"/>).</summary>
    public ContactProtector(byte[] masterKey)
    {
        if (masterKey.Length != StoreDataProtector.KeyBytes)
        {
            throw new ArgumentException($"Contact key must be {StoreDataProtector.KeyBytes} bytes.", nameof(masterKey));
        }
        _cipher = new StoreDataProtector(Subkey(masterKey, "enc"));
        _indexKey = Subkey(masterKey, "index");
    }

    // Same decoding rule as the store key: a 32-byte base64/hex value is used verbatim,
    // anything else is SHA-256'd — so operators configure Epass:ContactKey exactly like
    // Epass:StoreEncryptionKey.
    /// <summary>Derives the 32-byte master key from a configured <c>Epass:ContactKey</c> secret.</summary>
    public static byte[] DeriveKey(string secret) => StoreDataProtector.DeriveKey(secret);

    // AES-256-GCM ciphertext (enc:v1: envelope). Unprotect passes a value with no
    // versioned prefix through unchanged, so rows written before this rollout (dev
    // seed data) keep reading as their legacy plaintext.
    /// <summary>Encrypts a contact value for storage.</summary>
    public string Protect(string plaintext) => _cipher.Protect(plaintext);

    /// <summary>Decrypts a stored contact value; a value with no versioned prefix (pre-rollout legacy plaintext) passes through unchanged.</summary>
    public string Unprotect(string stored) => _cipher.Unprotect(stored);

    // Blind index for an email address: lowercased + trimmed, preserving the
    // case-insensitive equality/uniqueness the old CITEXT column gave.
    /// <summary>Blind index for an email address lookup: lowercased and trimmed before hashing, preserving the case-insensitive equality the old CITEXT column gave.</summary>
    public string EmailIndex(long realmId, string value) => Index(realmId, value.Trim().ToLowerInvariant());

    // Blind index for a phone number: trimmed only (values are already normalized
    // digits/E.164 on the way in; the old column was case-sensitive VARCHAR).
    /// <summary>Blind index for a phone number lookup: trimmed only, since values are already normalized digits/E.164 on the way in.</summary>
    public string PhoneIndex(long realmId, string value) => Index(realmId, value.Trim());

    // Realm-scoped, lowercase-hex HMAC-SHA-256 (64 hex chars). Binding the realm id into
    // the HMAC means the SAME address in two realms produces DIFFERENT indexes, so a
    // keyless DB dump cannot link one person across realms by matching hashes — the
    // realm-scoped lookups already pass the realm id, so this costs nothing at the call site.
    private string Index(long realmId, string normalized)
    {
        int n = Encoding.UTF8.GetByteCount(normalized);
        Span<byte> buf = n <= 256 ? stackalloc byte[8 + 256] : new byte[8 + n];
        BinaryPrimitives.WriteInt64BigEndian(buf, realmId);
        int written = Encoding.UTF8.GetBytes(normalized, buf[8..]);

        Span<byte> mac = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(_indexKey, buf[..(8 + written)], mac);
        return Convert.ToHexStringLower(mac);
    }

    private static byte[] Subkey(byte[] master, string label)
    {
        Span<byte> buffer = stackalloc byte[StoreDataProtector.KeyBytes + 8];
        master.CopyTo(buffer);
        int labelLen = Encoding.UTF8.GetBytes(label, buffer[StoreDataProtector.KeyBytes..]);
        return SHA256.HashData(buffer[..(StoreDataProtector.KeyBytes + labelLen)]);
    }
}
