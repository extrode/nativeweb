using System.Text.Encodings.Web;

using Microsoft.Extensions.FileProviders;

namespace __PRODUCT__.Middleware;

// The full-page chrome for error responses, loaded once at startup from
// wwwroot/pages/error.html (spec 003 R39): an error page must never depend on
// the renderer, the database, or anything else that can itself be the thing
// that just failed. Placeholders are literal-replaced; the values are the
// middleware's own constant strings, HTML-encoded anyway for defense in depth.
// Skeleton copy -- the product owns this file after stamping.
internal sealed class StaticErrorShell
{
    private const string TitleToken = "{{title}}";
    private const string MessageToken = "{{message}}";

    private readonly string _shell;

    internal StaticErrorShell(string shell) => _shell = shell;

    // Fail-fast at startup, matching the app's imperative config guards: a
    // missing shell discovered on the first 404 would be an error page that
    // itself 500s.
    public static StaticErrorShell Load(IWebHostEnvironment environment) =>
        Load(environment.WebRootFileProvider);

    internal static StaticErrorShell Load(IFileProvider webRoot)
    {
        IFileInfo file = webRoot.GetFileInfo("pages/error.html");
        if (!file.Exists)
        {
            throw new InvalidOperationException(
                "wwwroot/pages/error.html is missing; the static error shell is required (spec 003 R39).");
        }

        using Stream stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        return new StaticErrorShell(reader.ReadToEnd());
    }

    public string Render(string title, string message) => _shell
        .Replace(TitleToken, HtmlEncoder.Default.Encode(title), StringComparison.Ordinal)
        .Replace(MessageToken, HtmlEncoder.Default.Encode(message), StringComparison.Ordinal);
}
