using NativeWeb.Web.Middleware;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

public class StaticErrorShellTests
{
    [Fact]
    public void Render_ReplacesEveryOccurrenceOfBothTokens()
    {
        var shell = new StaticErrorShell("<title>{{title}}</title><h1>{{title}}</h1><p>{{message}}</p>");

        string html = shell.Render("Not found", "The page you're looking for doesn't exist.");

        Assert.Equal(
            "<title>Not found</title><h1>Not found</h1><p>The page you&#x27;re looking for doesn&#x27;t exist.</p>",
            html);
    }

    // The inputs are the middleware's own constants today, but the encoding is
    // load-bearing defense in depth: a future status description interpolating
    // anything request-derived must not become an injection.
    [Fact]
    public void Render_HtmlEncodesTheValues()
    {
        var shell = new StaticErrorShell("{{title}}|{{message}}");

        string html = shell.Render("<script>", "a & b");

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&amp;", html, StringComparison.Ordinal);
    }
}
