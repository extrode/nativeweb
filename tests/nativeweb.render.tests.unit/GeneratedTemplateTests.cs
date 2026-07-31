using NativeWeb.Render;
using NativeWeb.Render.Tests.Unit.Templates;

using Xunit;

using Index = NativeWeb.Render.Tests.Unit.Templates.Home.Index;

namespace NativeWeb.Render.Tests.Unit;

/// <summary>
/// Exercises the classes the generator produced from this project's templates/
/// directory: encoding-by-default, raw Html, layout inlining, slots/fills,
/// if/else, for, and partial calls.
/// </summary>
public sealed class GeneratedTemplateTests
{
    [Fact]
    public void Plain_EncodesStringParam_AndPassesHtmlParamRaw()
    {
        string html = Plain.ToHtmlString(Text: "<script>alert('x')</script>", Markup: Html.Raw("<em>ok</em>"));

        Assert.Contains("&lt;script&gt;alert(&#x27;x&#x27;)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("<div><em>ok</em></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Index_InlinesLayout_WithTitleAndFilledHeadSlot()
    {
        string html = Index.ToHtmlString(Title: "Home & Away", Message: "hi", ShowItems: false, Items: []);

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<title>Home &amp; Away</title>", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"description\" content=\"render-engine test page\">", html, StringComparison.Ordinal);
        Assert.Contains("<main>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Index_IfTrue_RendersLoopItemsEncoded()
    {
        string html = Index.ToHtmlString(Title: "t", Message: "m", ShowItems: true, Items: ["alpha", "<beta>"]);

        Assert.Contains("<li>alpha</li>", html, StringComparison.Ordinal);
        Assert.Contains("<li>&lt;beta&gt;</li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("No items.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Index_IfFalse_RendersElseBranch()
    {
        string html = Index.ToHtmlString(Title: "t", Message: "m", ShowItems: false, Items: []);

        Assert.Contains("<p>No items.</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<ul>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Index_PartialCall_RendersFormFieldWithArguments()
    {
        string html = Index.ToHtmlString(Title: "t", Message: "Search", ShowItems: false, Items: []);

        Assert.Contains("<label for=\"q\">Search</label>", html, StringComparison.Ordinal);
        Assert.Contains("<input id=\"q\" name=\"q\" type=\"text\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void FormField_IsAlsoCallableStandalone()
    {
        string html = Templates.Shared.FormField.ToHtmlString(Label: "A & B", Name: "field");

        Assert.Contains("<label for=\"field\">A &amp; B</label>", html, StringComparison.Ordinal);
    }
}
