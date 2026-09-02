namespace NativeWeb.Render;

/// <summary>
/// One response format the framework can write: the extension a URL names it by
/// and the media type the response declares (spec 021 R1, R5).
/// </summary>
/// <remarks>
/// The eight statics are the whole table. A negotiated route offers a subset of
/// them, <see cref="FormatResults"/> writes them, and <see cref="TryFromExtension"/>
/// is the one place an extension turns into a media type, so <c>/articles.json</c>
/// and <c>Accept: application/json</c> cannot drift apart.
/// </remarks>
/// <param name="Extension">The extension without its dot, <c>json</c>.</param>
/// <param name="MediaType">The media type without parameters, <c>application/json</c>.</param>
/// <param name="IsText">Whether the content type carries <c>charset=utf-8</c>.</param>
public readonly record struct Format(string Extension, string MediaType, bool IsText)
{
    /// <summary><c>text/html</c>, the default a page route offers.</summary>
    public static readonly Format Html = new("html", "text/html", true);

    /// <summary><c>text/plain</c>.</summary>
    public static readonly Format Text = new("txt", "text/plain", true);

    /// <summary><c>text/markdown</c>.</summary>
    public static readonly Format Markdown = new("md", "text/markdown", true);

    /// <summary><c>application/json</c>.</summary>
    public static readonly Format Json = new("json", "application/json", true);

    /// <summary><c>application/xml</c>, which is also what a sitemap is.</summary>
    public static readonly Format Xml = new("xml", "application/xml", true);

    /// <summary><c>application/atom+xml</c>.</summary>
    public static readonly Format Atom = new("atom", "application/atom+xml", true);

    /// <summary><c>application/rss+xml</c>.</summary>
    public static readonly Format Rss = new("rss", "application/rss+xml", true);

    /// <summary><c>text/csv</c>.</summary>
    public static readonly Format Csv = new("csv", "text/csv", true);

    private static readonly Format[] Known = [Html, Text, Markdown, Json, Xml, Atom, Rss, Csv];

    /// <summary>The <c>Content-Type</c> header value: the media type and its charset.</summary>
    public string ContentType => IsText ? MediaType + "; charset=utf-8" : MediaType;

    /// <summary>
    /// The format an extension names, case-insensitively and with or without its
    /// leading dot. Unknown extensions are not formats.
    /// </summary>
    /// <param name="extension"><c>json</c> or <c>.json</c>.</param>
    /// <param name="format">The matching format when there is one.</param>
    /// <returns>Whether <paramref name="extension"/> names a known format.</returns>
    public static bool TryFromExtension(string? extension, out Format format)
    {
        string wanted = (extension ?? "").TrimStart('.');

        foreach (Format candidate in Known)
        {
            if (string.Equals(candidate.Extension, wanted, StringComparison.OrdinalIgnoreCase))
            {
                format = candidate;
                return true;
            }
        }

        format = default;
        return false;
    }

    /// <summary>The media type, for messages and the 406 body.</summary>
    /// <returns>The media type without parameters.</returns>
    public override string ToString() => MediaType;
}
