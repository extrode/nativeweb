using System.Buffers;
using System.Text;

using NativeWeb.Render;

using Xunit;

namespace NativeWeb.Render.Tests.Unit;

public sealed class TemplateWriterTests
{
    private static string Render(TemplateRender render) => TemplateResults.ToHtmlString(render);

    [Fact]
    public void Write_String_EncodesHtmlSpecials()
    {
        string html = Render((ref TemplateWriter w) => w.Write("<b attr=\"x\">&'</b>"));

        Assert.Equal("&lt;b attr=&quot;x&quot;&gt;&amp;&#x27;&lt;/b&gt;", html);
    }

    [Fact]
    public void Write_StringWithoutSpecials_PassesThroughUnchanged()
    {
        string html = Render((ref TemplateWriter w) => w.Write("plain text 123"));

        Assert.Equal("plain text 123", html);
    }

    [Fact]
    public void Write_NullOrEmptyString_WritesNothing()
    {
        string html = Render((ref TemplateWriter w) =>
        {
            w.Write((string?)null);
            w.Write("");
        });

        Assert.Equal("", html);
    }

    [Fact]
    public void Write_NonAsciiText_TranscodesToUtf8()
    {
        string text = "héllo wörld — ✓ 日本語";
        string html = Render((ref TemplateWriter w) => w.Write(text));

        Assert.Equal(text, html);
    }

    [Fact]
    public void Write_LongString_SurvivesBufferChunking()
    {
        // Longer than the writer's 256-char transcode chunk, with a special at the end.
        string text = new string('à', 1000) + "<end>";
        string html = Render((ref TemplateWriter w) => w.Write(text));

        Assert.Equal(new string('à', 1000) + "&lt;end&gt;", html);
    }

    [Fact]
    public void Write_Html_IsNotEncoded()
    {
        string html = Render((ref TemplateWriter w) => w.Write(Html.Raw("<em>ok</em>")));

        Assert.Equal("<em>ok</em>", html);
    }

    [Fact]
    public void Write_HtmlEmpty_WritesNothing()
    {
        string html = Render((ref TemplateWriter w) => w.Write(Html.Empty));

        Assert.Equal("", html);
    }

    [Fact]
    public void Write_Char_EncodesSpecialsAndPassesPlain()
    {
        string html = Render((ref TemplateWriter w) =>
        {
            w.Write('A');
            w.Write('<');
            w.Write('é');
        });

        Assert.Equal("A&lt;é", html);
    }

    [Fact]
    public void Write_Numbers_FormatInvariant()
    {
        string html = Render((ref TemplateWriter w) =>
        {
            w.Write(-42);
            w.Write(9_007_199_254_740_993L);
        });

        Assert.Equal("-429007199254740993", html);
    }

    [Fact]
    public void WriteRaw_Utf8Literal_PassesThrough()
    {
        string html = Render((ref TemplateWriter w) => w.WriteRaw("<p>static</p>"u8));

        Assert.Equal("<p>static</p>", html);
    }

    [Fact]
    public void ToHtmlString_MatchesManualBufferRender()
    {
        ArrayBufferWriter<byte> buffer = new();
        TemplateWriter writer = new(buffer);
        writer.Write("a<b");
        string manual = Encoding.UTF8.GetString(buffer.WrittenSpan);

        Assert.Equal(manual, Render((ref TemplateWriter w) => w.Write("a<b")));
    }
}
