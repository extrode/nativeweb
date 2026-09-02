using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

using Xunit;

namespace NativeWeb.Render.Tests.Unit;

public sealed partial class FormatResultsTests
{
    // The spec's hostile title: markup, quotes, an ampersand, a comma, a newline and
    // a character outside the basic plane.
    internal const string HostileTitle = "<b>\"Q&A\" & Co,</b>\nline two \U0001F600";

    internal static async Task<(HttpResponse Response, byte[] Body)> ExecuteAsync(IResult result)
    {
        DefaultHttpContext http = new();
        MemoryStream body = new();
        http.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(body));

        await result.ExecuteAsync(http);

        return (http.Response, body.ToArray());
    }

    [Theory]
    [InlineData("json", "application/json")]
    [InlineData(".JSON", "application/json")]
    [InlineData("atom", "application/atom+xml")]
    [InlineData("rss", "application/rss+xml")]
    [InlineData("csv", "text/csv")]
    [InlineData("md", "text/markdown")]
    [InlineData("txt", "text/plain")]
    [InlineData("xml", "application/xml")]
    [InlineData("html", "text/html")]
    public void TryFromExtension_KnowsEveryFormat(string extension, string mediaType)
    {
        Assert.True(Format.TryFromExtension(extension, out Format format));
        Assert.Equal(mediaType, format.MediaType);
        Assert.Equal(mediaType + "; charset=utf-8", format.ContentType);
    }

    [Theory]
    [InlineData("exe")]
    [InlineData("a.b")]
    [InlineData("")]
    [InlineData(null)]
    public void TryFromExtension_RejectsWhatItDoesNotKnow(string? extension)
    {
        Assert.False(Format.TryFromExtension(extension, out _));
    }

    [Fact]
    public async Task Text_DeclaresTypeAndByteLength()
    {
        (HttpResponse response, byte[] body) = await ExecuteAsync(FormatResults.Text("héllo\n"));

        Assert.Equal("text/plain; charset=utf-8", response.ContentType);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(body.Length, response.ContentLength);
        Assert.Equal(7, body.Length);
        Assert.Equal("héllo\n", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task Text_CarriesTheStatusItIsGiven()
    {
        (HttpResponse response, byte[] body) = await ExecuteAsync(FormatResults.Text("Offered: text/html\n", 406));

        Assert.Equal(406, response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", response.ContentType);
        Assert.Equal(body.Length, response.ContentLength);
    }

    [Fact]
    public async Task Markdown_IsTextMarkdown()
    {
        (HttpResponse response, byte[] body) = await ExecuteAsync(FormatResults.Markdown("# Title\n"));

        Assert.Equal("text/markdown; charset=utf-8", response.ContentType);
        Assert.Equal(body.Length, response.ContentLength);
    }

    [Fact]
    public async Task Json_SerializesThroughTheSuppliedContractAndRoundTrips()
    {
        Row[] rows = [new Row("a1", HostileTitle)];

        (HttpResponse response, byte[] body) = await ExecuteAsync(
            FormatResults.Json(rows, TestJson.Default.RowArray));

        Assert.Equal("application/json; charset=utf-8", response.ContentType);
        Assert.Equal(body.Length, response.ContentLength);
        Row[]? back = JsonSerializer.Deserialize(body, TestJson.Default.RowArray);
        Assert.NotNull(back);
        Assert.Equal(HostileTitle, back[0].Title);
    }

    [Fact]
    public async Task Xml_EscapesStringsAndRoundTrips()
    {
        (HttpResponse response, byte[] body) = await ExecuteAsync(FormatResults.Xml(w =>
        {
            w.WriteStartElement("doc");
            w.WriteElementString("title", HostileTitle);
            w.WriteEndElement();
        }));

        Assert.Equal("application/xml; charset=utf-8", response.ContentType);
        Assert.Equal(body.Length, response.ContentLength);
        string text = Encoding.UTF8.GetString(body);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", text, StringComparison.Ordinal);
        var parsed = XDocument.Parse(text);
        Assert.Equal(HostileTitle, parsed.Root?.Element("title")?.Value);
    }

    [Fact]
    public async Task Xml_HasNoByteOrderMark()
    {
        (_, byte[] body) = await ExecuteAsync(FormatResults.Xml(w => w.WriteElementString("x", "y")));

        Assert.Equal((byte)'<', body[0]);
    }

    internal sealed record Row(string Id, string Title);

    [JsonSerializable(typeof(Row[]))]
    internal sealed partial class TestJson : JsonSerializerContext
    {
    }
}
