using System.Text;
using Geralt;

namespace NativeWeb.Security;

/// <summary>
/// Argon2id passphrase hashing (via Geralt / libsodium). Produces PHC-encoded
/// strings that embed the algorithm, parameters, and salt, so they self-describe
/// on verification and fit the schema's <c>hashed_passphrase VARCHAR(255)</c>.
/// </summary>
public static class Passphrases
{
    // OWASP Password Storage Cheat Sheet (2026), Argon2id recommendation:
    // m = 19456 KiB (19 MiB), t = 2, p = 1. libsodium fixes parallelism at 1,
    // so only the memory and iteration counts are configurable here.
    //
    // Geralt's Argon2id maps to libsodium crypto_pwhash: `iterations` is opslimit
    // and `memorySize` is memlimit in BYTES (not KiB). So 19456 KiB * 1024.
    private const int Iterations = 2;               // opslimit
    private const int MemorySizeKiB = 19456;        // 19 MiB per OWASP
    private const int MemorySize = MemorySizeKiB * 1024; // memlimit in bytes

    /// <summary>
    /// Hashes a passphrase with Argon2id and returns the PHC-encoded string
    /// (starts with <c>$argon2id$</c>, &lt;= 255 chars). A random salt is generated
    /// internally, so two calls with the same passphrase yield different hashes.
    /// </summary>
    /// <exception cref="ArgumentException">The passphrase is null or empty.</exception>
    public static string Hash(string passphrase)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        Span<byte> secret = ToUtf8(passphrase);
        try
        {
            Span<char> hash = stackalloc char[Argon2id.HashSize];
            Argon2id.ComputeHash(hash, secret, Iterations, MemorySize);
            return TrimNul(hash);
        }
        finally
        {
            secret.Clear();
        }
    }

    /// <summary>
    /// Constant-time verification of a passphrase against a stored PHC hash.
    /// Returns <see langword="true"/> on match, <see langword="false"/> if
    /// <paramref name="phcHash"/> isn't a well-formed Argon2id PHC string.
    /// </summary>
    /// <exception cref="ArgumentException">The passphrase is null or empty.</exception>
    public static bool Verify(string phcHash, string passphrase)
    {
        ArgumentException.ThrowIfNullOrEmpty(phcHash);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        Span<byte> secret = ToUtf8(passphrase);
        try
        {
            return Argon2id.VerifyHash(phcHash, secret);
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            secret.Clear();
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the stored hash was produced with
    /// weaker parameters than the current constants (memory or iterations), i.e.
    /// it should be re-hashed with <see cref="Hash"/> on next successful login.
    /// </summary>
    /// <exception cref="ArgumentException">The hash is null or empty.</exception>
    public static bool NeedsRehash(string phcHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(phcHash);
        return Argon2id.NeedsRehash(phcHash, Iterations, MemorySize);
    }

    private static byte[] ToUtf8(string value) => Encoding.UTF8.GetBytes(value);

    /// <summary>
    /// Geralt fills the fixed <see cref="Argon2id.HashSize"/> buffer and NUL-pads
    /// the remainder of the PHC string; return only the significant prefix.
    /// </summary>
    private static string TrimNul(ReadOnlySpan<char> hash)
    {
        int end = hash.IndexOf('\0');
        return new string(end < 0 ? hash : hash[..end]);
    }
}
