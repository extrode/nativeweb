using Xunit;

using Index = NativeWeb.Render.Tests.Unit.Templates.Home.Index;

namespace NativeWeb.Render.Tests.Unit;

/// <summary>
/// Spec 004 R9–R11: the generator's build-time pretty-printer. Goldens are exact —
/// indentation is the behavior under test. The formatter runs at generation time,
/// so these assertions also pin that no runtime formatting exists: the bytes ARE
/// the literals.
/// </summary>
public sealed class FormattingTests
{
    [Fact]
    public void Page_IsIndentedByNestingDepth_WithDirectiveBlankLinesRemoved()
    {
        string html = Index.ToHtmlString(Title: "T", Message: "Search", ShowItems: true, Items: ["a", "b"]);

        Assert.Equal(
            "<!DOCTYPE html>\n"
            + "<html lang=\"en\">\n"
            + "  <head>\n"
            + "    <meta charset=\"utf-8\">\n"
            + "    <title>T</title>\n"
            + "    <meta name=\"description\" content=\"render-engine test page\">\n"
            + "  </head>\n"
            + "  <body>\n"
            + "    <main>\n"
            + "      <h1>Search</h1>\n"
            + "      <ul>\n"
            + "        <li>a</li>\n"
            + "        <li>b</li>\n"
            + "      </ul>\n"
            + "      <label for=\"q\">Search</label>\n"
            + "      <input id=\"q\" name=\"q\" type=\"text\">\n"
            + "    </main>\n"
            + "  </body>\n"
            + "</html>\n",
            html);
    }

    [Fact]
    public void Page_HasNoBlankLinesAndNoTrailingSpaces()
    {
        string html = Index.ToHtmlString(Title: "t", Message: "m", ShowItems: false, Items: []);

        Assert.DoesNotContain("\n\n", html, StringComparison.Ordinal);
        foreach (string line in html.Split('\n'))
        {
            Assert.Equal(line.TrimEnd(), line);
        }
    }

    [Fact]
    public void Partial_IsIndentedPerCallSiteDepth()
    {
        // shared/form-field is called at depth 1 (format/shallow) and depth 3
        // (home/index inside html>body>main): each call site must get chunks
        // indented for its own depth, not one shared indentation.
        string shallow = Templates.Format.Shallow.ToHtmlString(Label: "L");

        Assert.Equal(
            "<div>\n"
            + "  <label for=\"s\">L</label>\n"
            + "  <input id=\"s\" name=\"s\" type=\"text\">\n"
            + "</div>\n",
            shallow);

        string deep = Index.ToHtmlString(Title: "t", Message: "m", ShowItems: false, Items: []);
        Assert.Contains("      <label for=\"q\">m</label>\n", deep, StringComparison.Ordinal);
    }

    [Fact]
    public void VerbatimElements_KeepTheirContentExactly()
    {
        string html = Templates.Format.Verbatim.ToHtmlString(Code: "c");

        Assert.Equal(
            "<div>\n"
            + "  <pre>\n"
            + "  keep   this\n"
            + "    exactly\n"
            + "</pre>\n"
            + "  <script>\n"
            + "  var x = 1;\n"
            + "    var y = 2;\n"
            + "</script>\n"
            + "  <p>c</p>\n"
            + "</div>\n",
            html);
    }

    [Fact]
    public void AmbiguousBranchDepths_BailOutToUnformattedOutput()
    {
        // format/bail opens <section> in one {{if}} and closes it in another, so
        // depth after each block is data-dependent. The formatter must leave the
        // whole template untouched (blank directive lines prove it did).
        string html = Templates.Format.Bail.ToHtmlString(Flag: true);

        Assert.Contains("\n\n", html, StringComparison.Ordinal);
        Assert.Contains("\n<section>\n", html, StringComparison.Ordinal);
    }
}
