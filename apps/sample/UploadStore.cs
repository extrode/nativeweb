using System.Security.Cryptography;

using NativeWeb.Forms;

namespace NativeWeb.Sample;

/// <summary>
/// Stores an uploaded picture under the configured directory and serves it back.
/// </summary>
/// <remarks>
/// <para>
/// Spec 027 R11 and R13. <b>The name the client sent never becomes a path.</b> It is
/// kept as metadata for the page to show and nothing else; what reaches the
/// filesystem is sixteen random bytes in hex plus an extension chosen from a fixed
/// table by content type. So there is no traversal to defend against here: a name of
/// <c>../../etc/passwd</c> is a string that gets HTML-encoded onto a page, and the
/// only path this type ever builds is one it made itself.
/// </para>
/// <para>
/// The serve side is checked twice anyway, because one guard is a guard nobody
/// notices removing: the id has to match <see cref="StoredName"/> exactly, and the
/// resolved full path has to sit directly under the configured directory. Either
/// alone would do; the pair survives someone loosening the other.
/// </para>
/// </remarks>
internal sealed class UploadStore(string directory)
{
    // The declared accepted set, and the extension each type is stored under. The
    // binder refuses anything outside the keys before a byte is written, so a lookup
    // miss here is unreachable rather than a fallback -- and it throws rather than
    // guessing an extension, because a stored file with the wrong one is a file
    // nothing can serve with the right content type afterwards.
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
    };

    private static readonly Dictionary<string, string> ContentTypes =
        Extensions.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The content types <c>[FormFile]</c> declares on the upload field.</summary>
    internal static string[] Accepted { get; } = [.. Extensions.Keys];

    /// <summary>Writes <paramref name="file"/> under a name this method chooses.</summary>
    /// <param name="file">The bound file.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>The stored name, which is what the serve route takes.</returns>
    internal async Task<string> StoreAsync(UploadedFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!Extensions.TryGetValue(file.ContentType, out string? extension))
        {
            throw new InvalidOperationException(
                $"The binder admitted content type '{file.ContentType}', which this store has no extension for.");
        }

        string stored = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + extension;

        await using FileStream target = File.Open(
            Path.Combine(directory, stored), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using Stream source = file.OpenReadStream();
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);

        return stored;
    }

    /// <summary>
    /// The full path of a stored upload, or <see langword="null"/> when
    /// <paramref name="id"/> is not one this store could have written.
    /// </summary>
    /// <param name="id">The stored name, from the URL.</param>
    /// <param name="contentType">What to serve it as.</param>
    /// <returns>The path, or null.</returns>
    internal string? Resolve(string? id, out string contentType)
    {
        contentType = string.Empty;

        if (id is null || !StoredName(id))
        {
            return null;
        }

        string path = Path.GetFullPath(Path.Combine(directory, id));

        // Belt to the pattern's braces. Path.GetFullPath is what would resolve a
        // traversal segment, so the answer is compared against the directory after
        // that resolution rather than before it.
        if (!string.Equals(Path.GetDirectoryName(path), Path.GetFullPath(directory), StringComparison.Ordinal))
        {
            return null;
        }

        contentType = ContentTypes[Path.GetExtension(path)];
        return File.Exists(path) ? path : null;
    }

    // Thirty-two lowercase hex characters and one extension from the table. Written
    // out rather than as a regex: the slim builder registers no regex constraint
    // resolver, and this runs on every request for an upload.
    private static bool StoredName(string id)
    {
        string extension = Path.GetExtension(id);

        if (!ContentTypes.ContainsKey(extension))
        {
            return false;
        }

        ReadOnlySpan<char> name = id.AsSpan(0, id.Length - extension.Length);

        if (name.Length != 32)
        {
            return false;
        }

        foreach (char character in name)
        {
            if (!char.IsAsciiDigit(character) && (character < 'a' || character > 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
