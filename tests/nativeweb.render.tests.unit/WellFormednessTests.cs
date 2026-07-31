using AngleSharp.Html.Parser;

using NativeWeb.Render;
using NativeWeb.Render.Tests.Unit.Templates;

using Xunit;

using Index = NativeWeb.Render.Tests.Unit.Templates.Home.Index;

namespace NativeWeb.Render.Tests.Unit;

/// <summary>
/// R13: rendered output must be well-formed HTML. AngleSharp's strict mode
/// throws on any parse error, so a passing test is the gate.
/// </summary>
public sealed class WellFormednessTests
{
    private static readonly HtmlParser StrictParser = new(new HtmlParserOptions { IsStrictMode = true });

    public static TheoryData<string, string> RenderedPages() => new()
    {
        { "index-with-items", Index.ToHtmlString(Title: "Title <&> \"quoted\"", Message: "msg '&'", ShowItems: true, Items: ["a", "<b>", "c & d"]) },
        { "index-empty", Index.ToHtmlString(Title: "t", Message: "m", ShowItems: false, Items: []) },
    };

    [Theory]
    [MemberData(nameof(RenderedPages))]
    public void RenderedDocument_ParsesInStrictMode(string name, string html)
    {
        _ = name;
        _ = StrictParser.ParseDocument(html);
    }

    [Fact]
    public void HostileParamValues_CannotBreakDocumentStructure()
    {
        string html = Index.ToHtmlString(
            Title: "</title><script>alert(1)</script>",
            Message: "\"><img src=x onerror=alert(1)>",
            ShowItems: true,
            Items: ["</li></ul></main></body></html>"]);

        AngleSharp.Html.Dom.IHtmlDocument document = StrictParser.ParseDocument(html);
        Assert.Empty(document.QuerySelectorAll("script"));
        Assert.Empty(document.QuerySelectorAll("img"));
    }

    [Fact]
    public void Fragment_Plain_ParsesInStrictMode()
    {
        // Fragments are parsed in a body context; strict mode still flags tag soup.
        string html = Plain.ToHtmlString(Text: "x < y & z", Markup: Html.Raw("<em>fine</em>"));
        _ = StrictParser.ParseDocument("<!DOCTYPE html><html><head><title>f</title></head><body>" + html + "</body></html>");
    }
}
