using Geralt;

namespace NativeWeb.Security;

/// <summary>
/// Primitives for opaque bearer tokens (access tokens, secrets).
/// </summary>
/// <remarks>
/// The plaintext token from <see cref="Generate"/> is what the client holds and
/// presents on each request. It is NEVER stored. Persist only <see cref="Hash"/>
/// of the token; on lookup, hash the presented token and compare against the
/// stored <c>token_hash</c> (schema: <c>CHAR(64)</c>, SHA-256 lowercase hex).
/// A leaked database therefore never yields usable tokens.
/// </remarks>
public static class OpaqueTokens
{
    /// <summary>Entropy of a freshly generated token, in bytes (256 bits).</summary>
    private const int TokenBytes = 32;

    /// <summary>
    /// Generates a new plaintext bearer token: 32 bytes of CSPRNG output encoded
    /// as unpadded base64url (43 characters). This value is returned to the client
    /// and must never be persisted; store only <see cref="Hash"/> of it.
    /// </summary>
    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[TokenBytes];
        SecureRandom.Fill(bytes);

        int length = Encodings.GetToBase64BufferSize(bytes, Encodings.Base64Variant.UrlNoPadding);
        Span<char> encoded = stackalloc char[length];
        Encodings.ToBase64(encoded, bytes, Encodings.Base64Variant.UrlNoPadding);
        return new string(encoded);
    }

    /// <summary>
    /// Computes the at-rest form of a token: SHA-256 of its UTF-8 bytes, rendered
    /// as 64-character lowercase hex. Matches the schema's <c>token_hash CHAR(64)</c>
    /// convention used for equality lookups. SHA-256 (not Argon2id) is correct here
    /// because the token itself is 256 bits of uniform CSPRNG entropy — there is
    /// nothing to brute-force, so a slow KDF would add cost without benefit.
    /// </summary>
    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return Sha256Hex.OfUtf8(token);
    }
}
