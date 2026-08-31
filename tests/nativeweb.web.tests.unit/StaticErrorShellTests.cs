using Microsoft.Extensions.FileProviders;

using __PRODUCT__.Middleware;

using Xunit;

namespace NativeWeb.Web.Tests.Unit;

public class StaticErrorShellTests
{
    // The fail-fast guard is the whole point of Load: a missing shell must
    // kill startup, not surface as an error page that itself 500s.
    [Fact]
    public void Load_MissingShellFile_FailsFastWithAPointedMessage()
    {
        InvalidOperationException ex =
            Assert.Throws<InvalidOperationException>(() => StaticErrorShell.Load(new NullFileProvider()));

        Assert.Contains("pages/error.html", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_PresentShellFile_ReadsItsContent()
    {
        string root = Directory.CreateTempSubdirectory("error-shell-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "pages"));
            File.WriteAllText(Path.Combine(root, "pages", "error.html"), "<h1>{{title}}</h1>");
            using var provider = new PhysicalFileProvider(root);

            var shell = StaticErrorShell.Load(provider);

            Assert.Equal("<h1>Not found</h1>", shell.Render("Not found", "unused"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
