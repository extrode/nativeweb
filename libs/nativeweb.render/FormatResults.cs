using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Xml;

using Microsoft.AspNetCore.Http;

namespace NativeWeb.Render;

/// <summary>
/// One call per format (spec 021 R1). Every result here is a buffered UTF-8 body
/// with its length declared and written once, for the reason
/// <see cref="TemplateResults.Html"/> gives: an undeclared length makes the
/// response chunked, and that framing cost 35% of a page's throughput when it was
/// measured on 2026-08-31.
/// </summary>
/// <remarks>
/// <para>
/// Escaping is the helper's job, not the caller's (R3). JSON goes through the
/// serializer, the XML family through <see cref="XmlWriter"/>, CSV through
/// <see cref="CsvWriter"/>. The one opt-out is <see cref="Xml(Action{XmlWriter})"/>:
/// <c>WriteString</c> and <c>WriteElementString</c> escape, <c>WriteRaw</c> and
/// <c>WriteCData</c> do not, and a caller who reaches for those owns the result.
/// </para>
/// <para>
/// Nothing here reflects (R2). JSON takes a <see cref="JsonTypeInfo{T}"/> from a
/// source-generated context, which is the only serializer a NativeAOT binary
/// under <c>TreatWarningsAsErrors</c> can carry.
/// </para>
/// </remarks>
public static class FormatResults
{
    private static readonly XmlWriterSettings XmlSettings = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        OmitXmlDeclaration = false,
        CloseOutput = false,
    };

    /// <summary><c>text/plain</c>.</summary>
    /// <param name="body">The document.</param>
    /// <returns>The buffered result.</returns>
    public static IResult Text(string body) => Buffered(Format.Text, body);

    /// <summary><c>text/plain</c> with a status other than 200: a refusal that says why.</summary>
    /// <param name="body">The document.</param>
    /// <param name="statusCode">The status to answer with.</param>
    /// <returns>The buffered result.</returns>
    public static IResult Text(string body, int statusCode) => Buffered(Format.Text, body, statusCode);

    /// <summary><c>text/markdown</c>.</summary>
    /// <param name="body">The document.</param>
    /// <returns>The buffered result.</returns>
    public static IResult Markdown(string body) => Buffered(Format.Markdown, body);

    /// <summary>
    /// <c>application/json</c>, serialized through a source-generated contract.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="typeInfo">The contract from the caller's <c>JsonSerializerContext</c>.</param>
    /// <returns>The buffered result.</returns>
    public static IResult Json<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        return new BufferedResult(Format.Json, JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));
    }

    /// <summary>
    /// <c>application/xml</c>, written by the caller through an <see cref="XmlWriter"/>
    /// that escapes everything written as a string.
    /// </summary>
    /// <param name="write">Writes the document; the declaration is already there.</param>
    /// <returns>The buffered result.</returns>
    public static IResult Xml(Action<XmlWriter> write) => Xml(Format.Xml, write);

    /// <summary><c>text/csv</c>, RFC 4180, header row first, formula cells neutralised.</summary>
    /// <param name="headers">The header row.</param>
    /// <param name="rows">The data rows, each as many cells as there are headers.</param>
    /// <returns>The buffered result.</returns>
    public static IResult Csv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        return Buffered(Format.Csv, CsvWriter.Write(headers, rows));
    }

    /// <summary>Atom 1.0, links resolved against <paramref name="baseUrl"/>.</summary>
    /// <param name="feed">The feed.</param>
    /// <param name="baseUrl">The absolute origin every path in the feed resolves against.</param>
    /// <returns>The buffered result.</returns>
    public static IResult Atom(Feed feed, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(baseUrl);

        return Xml(Format.Atom, w => FeedWriter.Atom(w, feed, baseUrl));
    }

    /// <summary>Atom 1.0, links resolved against the request's public origin.</summary>
    /// <param name="feed">The feed.</param>
    /// <param name="request">The current request.</param>
    /// <returns>The buffered result.</returns>
    public static IResult Atom(Feed feed, HttpRequest request) => Atom(feed, request.PublicBase());

    /// <summary>RSS 2.0, links resolved against <paramref name="baseUrl"/>.</summary>
    /// <param name="feed">The feed.</param>
    /// <param name="baseUrl">The absolute origin every path in the feed resolves against.</param>
    /// <returns>The buffered result.</returns>
    public static IResult Rss(Feed feed, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(baseUrl);

        return Xml(Format.Rss, w => FeedWriter.Rss(w, feed, baseUrl));
    }

    /// <summary>RSS 2.0, links resolved against the request's public origin.</summary>
    /// <param name="feed">The feed.</param>
    /// <param name="request">The current request.</param>
    /// <returns>The buffered result.</returns>
    public static IResult Rss(Feed feed, HttpRequest request) => Rss(feed, request.PublicBase());

    /// <summary>A sitemaps.org sitemap, locations resolved against <paramref name="baseUrl"/>.</summary>
    /// <param name="entries">The pages.</param>
    /// <param name="baseUrl">The absolute origin every path resolves against.</param>
    /// <returns>The buffered result, as <c>application/xml</c>.</returns>
    public static IResult Sitemap(IEnumerable<SitemapEntry> entries, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(baseUrl);

        return Xml(Format.Xml, w => FeedWriter.Sitemap(w, entries, baseUrl));
    }

    /// <summary>A sitemaps.org sitemap, locations resolved against the request's public origin.</summary>
    /// <param name="entries">The pages.</param>
    /// <param name="request">The current request.</param>
    /// <returns>The buffered result, as <c>application/xml</c>.</returns>
    public static IResult Sitemap(IEnumerable<SitemapEntry> entries, HttpRequest request) =>
        Sitemap(entries, request.PublicBase());

    internal static IResult Xml(Format format, Action<XmlWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        ArrayBufferWriter<byte> buffer = new(4096);
        using (var stream = new BufferStream(buffer))
        using (var writer = XmlWriter.Create(stream, XmlSettings))
        {
            write(writer);
            writer.Flush();
        }

        return new BufferedResult(format, buffer.WrittenSpan.ToArray());
    }

    internal static IResult Buffered(Format format, string body, int statusCode = StatusCodes.Status200OK) =>
        new BufferedResult(format, Encoding.UTF8.GetBytes(body), statusCode);

    // Same shape as TemplateHtmlResult: type, length, one write, one flush.
    private sealed class BufferedResult(Format format, byte[] body, int statusCode = StatusCodes.Status200OK) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = format.ContentType;
            httpContext.Response.ContentLength = body.Length;
            httpContext.Response.BodyWriter.Write(body);
            return httpContext.Response.BodyWriter.FlushAsync(httpContext.RequestAborted).AsTask();
        }
    }

    // XmlWriter wants a Stream; the body wants an ArrayBufferWriter. Write-only,
    // no seeking, and Flush is a no-op because every byte is already in the buffer.
    private sealed class BufferStream(ArrayBufferWriter<byte> buffer) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => buffer.WrittenCount;
        public override long Position { get => buffer.WrittenCount; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> source) => buffer.Write(source);
    }
}
