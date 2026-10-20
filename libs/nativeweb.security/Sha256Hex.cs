using System.Security.Cryptography;
using System.Text;

namespace NativeWeb.Security;

// The codebase's single at-rest hash routine: SHA-256 of a string's UTF-8 bytes,
// rendered as 64-char lowercase hex (the CHAR(64) *_hash convention). This is the most
// security-critical shared format in the system, so it lives in exactly one place;
// callers apply their own preconditions (reject-empty, lowercase, etc.) before calling.
public static class Sha256Hex
{
    public static string OfUtf8(string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> utf8 = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(value, utf8);

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(utf8, digest);
        return Convert.ToHexStringLower(digest);
    }
}
