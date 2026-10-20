using System.Buffers;
using System.Text;

using Microsoft.AspNetCore.Http;

namespace NativeWeb.Render;

/// <summary>Renders a template body into a <see cref="TemplateWriter"/>.</summary>
public delegate void TemplateRender(ref TemplateWriter writer);

/// <summary>
/// Adapters from generated template code to the two consumers: an HTTP response
/// (UTF-8 with a declared length) and a materialized string (e-mail bodies, tests,
/// and the htmx fragment paths that still compose strings).
/// </summary>
public static class TemplateResults
{
    public static IResult Html(TemplateRender render) => new TemplateHtmlResult(render);

    public static string ToHtmlString(TemplateRender render)
    {
        ArrayBufferWriter<byte> buffer = new(4096);
        TemplateWriter writer = new(buffer);
        render(ref writer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private sealed class TemplateHtmlResult(TemplateRender render) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            // Render into a private buffer first, then declare the length and hand
            // the whole body over in one write.
            //
            // This used to write straight into Response.BodyWriter and flush once,
            // which reads like the cheaper path and is not. Measured 2026-08-31,
            // apps/sample GET /articles, 16 workers, three variants of the same
            // page:
            //
            //   straight to BodyWriter, no length     11,779 req/s   p50 1.24ms
            //   buffered here, no length              11,778 req/s   p50 1.24ms
            //   buffered here, length set             18,105 req/s   p50 0.71ms
            //
            // The middle row is what makes this readable: buffering changes
            // nothing, so the cost was never the many small GetSpan/Advance calls
            // into Kestrel's pipe. It is the absent Content-Length, which makes the
            // response chunked -- and a chunked page costs 35% of its throughput
            // for a framing no page needs. The body was already fully materialized
            // before the flush either way, so nothing was being streamed and the
            // length was always knowable; it was never being said.
            //
            // Fine for pages, which is what this type is for. A genuinely unbounded
            // stream cannot know its length and needs a different result type --
            // one that streams for real, rather than buffering and staying silent.
            httpContext.Response.ContentType = "text/html; charset=utf-8";

            ArrayBufferWriter<byte> body = new(4096);
            TemplateWriter writer = new(body);
            render(ref writer);

            httpContext.Response.ContentLength = body.WrittenCount;
            httpContext.Response.BodyWriter.Write(body.WrittenSpan);
            return httpContext.Response.BodyWriter.FlushAsync(httpContext.RequestAborted).AsTask();
        }
    }
}
