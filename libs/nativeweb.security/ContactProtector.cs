using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NativeWeb.Security;

// At-rest protection for contact PII — an email address or phone number a product
// stores. Unlike hashed secrets, a contact value must round-trip in plaintext (a code
// or link is sent to it, and it is displayed masked), so it is *encrypted*, not hashed.
// But encryption is non-deterministic (fresh nonce per write), so it cannot back an
// equality lookup or a UNIQUE constraint. Those move to a **blind index**: a keyed HMAC
// of the normalized value, deterministic and non-reversible, stored beside the
// ciphertext and queried instead of the plaintext.
//
// Two independent subkeys are derived from one configured secret — supplied by the
// product, which names the key, since this library reads no configuration — so the
// encryption key and the index key are never the same value:
//   master   = StoreDataProtector.DeriveKey(secret)   (32 bytes)
//   encKey   = SHA-256(master ‖ "enc")    -> AES-256-GCM (via StoreDataProtector)
//   indexKey = SHA-256(master ‖ "index")  -> HMAC-SHA-256 blind index
//
// A third subkey, "fakemask", and the FakeMaskDigest method over it were removed on
// 2026-09-01. They existed only to derive the identity provider's enumeration-defense
// fake mask — a piece of that product's sign-in response flow rather than a primitive,
// and so past the line AGENTS.md draws. Removing it changes no index or ciphertext:
// the enc and index subkeys are derived from their own labels and are untouched.
/// <summary>
/// At-rest protection for contact PII (an email address or phone number): AES-256-GCM
/// encryption for plaintext round-trip, plus a scope-bound HMAC-SHA-256 blind index for
/// equality lookup and uniqueness, since the ciphertext itself cannot back either.
/// Encryption and index keys are independent subkeys of one secret the product supplies
/// and names.
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
    // anything else is SHA-256'd — so whatever a product calls its contact key, an operator
    // supplies it the same way as the store encryption key.
    /// <summary>Derives the 32-byte master key from the configured contact secret.</summary>
    public static byte[] DeriveKey(string secret) => StoreDataProtector.DeriveKey(secret);

    /// <summary>Encrypts a contact value for storage.</summary>
    public string Protect(string plaintext) => _cipher.Protect(plaintext);

    /// <summary>Decrypts a stored contact value; a value without the versioned prefix is refused.</summary>
    public string Unprotect(string stored) => _cipher.Unprotect(stored);

    // Blind index for an email address: lowercased + trimmed, so the index gives the
    // case-insensitive equality and uniqueness a case-insensitive column would.
    /// <summary>Blind index for an email address lookup: lowercased and trimmed before hashing, so equality is case-insensitive.</summary>
    public string EmailIndex(long scopeId, string value) => Index(scopeId, value.Trim().ToLowerInvariant());

    // Blind index for a phone number: trimmed only, since a number carries no case and
    // is expected to arrive already normalized to digits or E.164.
    /// <summary>Blind index for a phone number lookup: trimmed only, since values are expected to be normalized digits or E.164 on the way in.</summary>
    public string PhoneIndex(long scopeId, string value) => Index(scopeId, value.Trim());

    // Scope-bound, lowercase-hex HMAC-SHA-256 (64 hex chars). Binding a scope id into the
    // HMAC means the SAME address under two scopes produces DIFFERENT indexes, so a keyless
    // database dump cannot link one person across tenants by matching hashes. A product with
    // no tenancy passes a constant — 0 is as good as any, and nothing here special-cases it.
    private string Index(long scopeId, string normalized)
    {
        int n = Encoding.UTF8.GetByteCount(normalized);
        Span<byte> buf = n <= 256 ? stackalloc byte[8 + 256] : new byte[8 + n];
        BinaryPrimitives.WriteInt64BigEndian(buf, scopeId);
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
