using System.Buffers;
using System.Text;

using Microsoft.AspNetCore.Http;

namespace NativeWeb.Render;

/// <summary>Renders a template body into a <see cref="TemplateWriter"/>.</summary>
public delegate void TemplateRender(ref TemplateWriter writer);

/// <summary>
/// Adapters from generated template code to the two consumers: an HTTP response
/// (UTF-8 straight into the body writer) and a materialized string (e-mail bodies,
/// tests, and the htmx fragment paths that still compose strings).
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
            // The sync ref-struct render path cannot await, so bytes accumulate in
            // the BodyWriter until the single flush below. Fine for pages; a
            // genuinely unbounded stream would need a different result type.
            httpContext.Response.ContentType = "text/html; charset=utf-8";
            TemplateWriter writer = new(httpContext.Response.BodyWriter);
            render(ref writer);
            return httpContext.Response.BodyWriter.FlushAsync(httpContext.RequestAborted).AsTask();
        }
    }
}
