using Microsoft.AspNetCore.Http;

namespace NativeWeb.Forms;

/// <summary>
/// One uploaded file, as a bound form model sees it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not <see cref="IFormFile"/>.</b> The whole point of a generated
/// binder is that a handler never reaches into <see cref="HttpContext"/>: a model
/// exposing <see cref="IFormFile"/> would hand back a type whose
/// <c>OpenReadStream</c> is only valid while the request's form is still buffered,
/// and whose other members (<c>Headers</c>, <c>ContentDisposition</c>) are the raw
/// request again by another route. Four properties and a stream is the whole of
/// what a handler needs, and it is a shape a test can construct without a server.
/// </para>
/// <para>
/// <b><see cref="Name"/> is the client's, and it is not a path.</b> It arrives from
/// the browser, so it may contain directory separators, <c>..</c>, a drive letter, a
/// NUL, or nothing at all. Treat it as a label to show a person. Anything writing to
/// disk generates its own name -- <c>apps/sample/UploadStore.cs</c> is the worked
/// example.
/// </para>
/// <para>
/// The stream is the request's buffered content, so it is valid for the duration of
/// the request and no longer. A handler that needs the bytes past that copies them.
/// </para>
/// </remarks>
public sealed class FormFile
{
    private readonly IFormFile _file;

    internal FormFile(IFormFile file)
    {
        _file = file;
    }

    /// <summary>
    /// The file name the client supplied. Never a path: see the type's remarks.
    /// </summary>
    public string Name => _file.FileName;

    /// <summary>
    /// The content type the client declared, or <c>""</c> when it declared none.
    /// </summary>
    /// <remarks>
    /// The client's claim, not an inspection of the bytes. A
    /// <c>[FormFile(Accept = ...)]</c> declaration is checked against this, which
    /// keeps an honest client from posting the wrong kind of file by accident and
    /// does nothing at all against a dishonest one.
    /// </remarks>
    public string ContentType => _file.ContentType ?? string.Empty;

    /// <summary>The length in bytes.</summary>
    public long Length => _file.Length;

    /// <summary>
    /// Opens the content for reading. Valid for the duration of the request.
    /// </summary>
    /// <returns>A readable stream over the file's bytes.</returns>
    public Stream OpenReadStream() => _file.OpenReadStream();
}
