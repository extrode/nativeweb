using System.Diagnostics;
using System.Text.Encodings.Web;

using Microsoft.Extensions.FileProviders;

namespace NativeWeb.Web;

// The full-page chrome for error responses, loaded once at startup from
// wwwroot/pages/error.html (spec 003 R39): an error page must never depend on
// the renderer, the database, or anything else that can itself be the thing
// that just failed. Placeholders are literal-replaced; the values are the
// middleware's own constant strings, HTML-encoded anyway for defense in depth.
// Skeleton copy -- the product owns this file after stamping.
public sealed class StaticErrorShell
{
    private const string TitleToken = "{{title}}";
    private const string MessageToken = "{{message}}";

    private readonly string _shell;

    public StaticErrorShell(string shell) => _shell = shell;

    // Fail-fast at startup, matching the app's imperative config guards: a
    // missing shell discovered on the first 404 would be an error page that
    // itself 500s.
    //
    // This is the overload NativeWebPipeline calls, which puts it on the path out
    // of Main -- where an escaping exception is the modal-block crash
    // StartupConfigGuards.FailStartup exists to prevent, not a clean refusal. A
    // missing or unreadable shell is an operator error with a known remedy, so it
    // leaves by the same stderr message and exit 78 as a refused configuration
    // value. The IFileProvider overload keeps throwing: it stays a pure function
    // the unit tier can drive, and a test cannot observe Environment.Exit.
    public static StaticErrorShell Load(IWebHostEnvironment environment)
    {
        try
        {
            return Load(environment.WebRootFileProvider);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            StartupConfigGuards.FailStartup(ex.Message);

            // [DoesNotReturn] drives nullable flow analysis, not CS0161's
            // definite-return check, so the compiler still wants an exit here.
            // FailStartup itself carries the same line for the same reason.
            throw new UnreachableException();
        }
    }

    public static StaticErrorShell Load(IFileProvider webRoot)
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
