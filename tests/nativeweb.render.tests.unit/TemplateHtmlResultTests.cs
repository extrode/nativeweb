using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

using Xunit;

namespace NativeWeb.Render.Tests.Unit;

/// <summary>
/// <see cref="TemplateResults.Html"/> writes the whole body and says how long it
/// is.
/// </summary>
/// <remarks>
/// <para>
/// The length is the point. Until 2026-08-31 this path wrote into
/// <c>Response.BodyWriter</c> and flushed once, which left no
/// <c>Content-Length</c> and so made every page a chunked response. Measured on
/// <c>apps/sample</c>'s <c>GET /articles</c> at 16 workers: 11,779 req/s without
/// the header against 18,105 with it, same bytes, same buffering. A third variant
/// -- buffered but still silent about the length -- measured 11,778, which is what
/// rules out the write pattern and leaves the framing.
/// </para>
/// <para>
/// No suite would have noticed. A chunked response and a measured one are the
/// same page to every assertion anyone writes, which is why the guard is on the
/// header rather than on the markup, and why it lives here rather than in a
/// benchmark nobody runs on a branch.
/// </para>
/// </remarks>
public sealed class TemplateHtmlResultTests
{
    private static async Task<(HttpResponse Response, string Body)> ExecuteAsync(TemplateRender render)
    {
        DefaultHttpContext http = new();
        MemoryStream body = new();
        http.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(body));

        await TemplateResults.Html(render).ExecuteAsync(http);

        return (http.Response, System.Text.Encoding.UTF8.GetString(body.ToArray()));
    }

    [Fact]
    public async Task Html_DeclaresTheBodyLength()
    {
        (HttpResponse response, string body) = await ExecuteAsync(
            (ref TemplateWriter w) => w.Write("hello"));

        Assert.Equal("hello", body);
        Assert.Equal(5, response.ContentLength);
    }

    /// <summary>
    /// The length is the encoded byte count, not the character count.
    /// </summary>
    /// <remarks>
    /// Two ways to get this wrong produce a number that looks right in ASCII and
    /// truncates or hangs the response otherwise: counting characters before
    /// encoding, and counting them after HTML-encoding into entities. A
    /// three-character string that is five bytes and eleven bytes of markup
    /// separates all three.
    /// </remarks>
    [Fact]
    public async Task Html_CountsEncodedBytesRatherThanCharacters()
    {
        (HttpResponse response, string body) = await ExecuteAsync(
            (ref TemplateWriter w) => w.Write("é<b"));

        Assert.Equal("é&lt;b", body);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(body), response.ContentLength);
        Assert.NotEqual(3, response.ContentLength);
    }

    [Fact]
    public async Task Html_SetsTheHtmlContentType()
    {
        (HttpResponse response, _) = await ExecuteAsync((ref TemplateWriter w) => w.Write("x"));

        Assert.Equal("text/html; charset=utf-8", response.ContentType);
    }

    /// <summary>An empty template is a zero-length body, not an absent one.</summary>
    [Fact]
    public async Task Html_WithNothingWritten_DeclaresZeroRatherThanLeavingItUnset()
    {
        (HttpResponse response, string body) = await ExecuteAsync((ref TemplateWriter w) => { });

        Assert.Equal("", body);
        Assert.Equal(0, response.ContentLength);
    }
}
