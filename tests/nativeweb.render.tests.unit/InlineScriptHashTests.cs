using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

using System.Security.Cryptography;
using System.Text;

using Xunit;

namespace NativeWeb.Render.Tests.Unit;

/// <summary>
/// Spec 006 R3: every static inline script gets a CSP hash source computed from
/// the exact bytes Render writes, so a cacheable page stays script-src
/// authorized without a per-response nonce. The proof is a recomputation from
/// the RENDERED output — the constant must equal the hash of the served bytes.
/// </summary>
public sealed class InlineScriptHashTests
{
    [Fact]
    public void StaticScriptBody_HashesTheRenderedBytes_DespiteADynamicNonce()
    {
        string html = Templates.Csp.Inline.ToHtmlString(Nonce: "r4nd0m");

        string body = Assert.Single(RenderedScriptBodies(html));
        Assert.Equal(Sha256Source(body), Assert.Single(Templates.Csp.Inline.InlineScriptCspHashes));
    }

    // The fixture is CRLF on disk and pinned that way in .gitattributes. The
    // browser newline-normalizes the input stream before tokenizing, so the
    // text node CSP hashes is LF-only however the file was authored; hashing
    // the authored bytes would make the constant depend on the line endings of
    // the checkout, and the CRLF one blocks its own script everywhere.
    [Fact]
    public void ACrlfAuthoredScript_IsHashedAsTheBrowserSeesIt()
    {
        string body = Assert.Single(RenderedScriptBodies(Templates.Csp.Crlf.ToHtmlString()));
        Assert.Contains('\r', body);

        string hash = Assert.Single(Templates.Csp.Crlf.InlineScriptCspHashes);
        Assert.Equal(Sha256Source(body.Replace("\r\n", "\n")), hash);
        Assert.NotEqual(Sha256Source(body), hash);
    }

    [Fact]
    public void ScriptWithADynamicBody_IsOmitted()
    {
        // The script renders — it just cannot be authorized by a build-time hash.
        Assert.Contains("var v = \"v\";", Templates.Csp.Dynamic.ToHtmlString(Value: "v"), StringComparison.Ordinal);
        Assert.Empty(Templates.Csp.Dynamic.InlineScriptCspHashes);
    }

    [Fact]
    public void TemplateWithoutScripts_HasAnEmptyCollection() =>
        Assert.Empty(Templates.Plain.InlineScriptCspHashes);

    [Fact]
    public void TwoStaticScripts_AreHashedInDocumentOrder()
    {
        List<string> bodies = RenderedScriptBodies(Templates.Csp.Two.ToHtmlString());

        Assert.Collection(
            bodies,
            first => Assert.Contains("var a = 1;", first, StringComparison.Ordinal),
            second => Assert.Contains("var b = 2;", second, StringComparison.Ordinal));
        Assert.Equal(bodies.Select(Sha256Source), Templates.Csp.Two.InlineScriptCspHashes);
    }

    /// <summary>
    /// '&lt;/script' only ends the element when the name really ends there.
    /// The expected body is written out literally rather than extracted, so
    /// this test cannot agree with the scanner by sharing its framing rules —
    /// getting the boundary wrong here truncates the body at '&lt;/scripting'
    /// and produces a hash that authorizes bytes the browser never sees.
    /// </summary>
    [Fact]
    public void CloseTagInsideAStringLiteral_DoesNotEndTheBody()
    {
        const string body = "\n    var s = \"</scripting\";\n    var t = 1;\n  ";

        Assert.Contains("<script>" + body + "</script>", Templates.Csp.Closetag.ToHtmlString(), StringComparison.Ordinal);
        Assert.Equal(Sha256Source(body), Assert.Single(Templates.Csp.Closetag.InlineScriptCspHashes));
    }

    /// <summary>
    /// An apostrophe in an unquoted attribute value is an ordinary character,
    /// so the tag still ends at its '>'. Reading it as a quote would swallow
    /// the '>' and frame the body from somewhere inside the script, hiding the
    /// dynamic interpolation from the guard — this template's body is dynamic
    /// and must therefore go unhashed.
    /// </summary>
    [Fact]
    public void UnquotedAttributeWithAnApostrophe_DoesNotShiftTheBodyBoundary()
    {
        Assert.Contains("var v = \"V\";", Templates.Csp.Unquoted.ToHtmlString(Value: "V"), StringComparison.Ordinal);
        Assert.Empty(Templates.Csp.Unquoted.InlineScriptCspHashes);
    }

    /// <summary>
    /// A data block is never evaluated, so hashing it would add an allowlist
    /// entry authorizing that exact string as executable script anywhere on the
    /// page, for nothing.
    /// </summary>
    [Fact]
    public void NonExecutableScriptType_IsNotHashed() =>
        Assert.Empty(Templates.Csp.Json.InlineScriptCspHashes);

    /// <summary>
    /// The legacy JavaScript MIME types still execute, so they still need a
    /// hash. This failure direction is the expensive one: an executing script
    /// the scanner does not recognise gets no hash and is blocked by the very
    /// CSP that was meant to allow it — in production only, with every other
    /// test in this file green.
    /// </summary>
    /// <remarks>
    /// The expected bodies are written out by hand rather than extracted from
    /// the render, so this cannot agree with the scanner by sharing its rules.
    /// 'text/template' is the control: it is in the same fixture and must stay
    /// unhashed, so a scanner that simply hashed every script would fail here
    /// rather than pass six times over.
    /// </remarks>
    [Fact]
    public void LegacyJavaScriptMimeTypes_AreStillHashed()
    {
        string[] executable =
        [
            "\n    var jscript = 1;\n  ",
            "\n    var xjavascript = 2;\n  ",
            "\n    var livescript = 3;\n  ",
            "\n    var versioned = 4;\n  ",
            "\n    var xecmascript = 5;\n  ",
            "\n    var cased = 6;\n  ",
        ];

        Assert.Equal(
            executable.Select(Sha256Source),
            Templates.Csp.Legacytypes.InlineScriptCspHashes);

        Assert.DoesNotContain(
            Sha256Source("\n    var neverRun = 7;\n  "),
            Templates.Csp.Legacytypes.InlineScriptCspHashes);
    }

    /// <summary>
    /// '&lt;!--' puts the browser in script-data-escaped state, where the next
    /// '&lt;/script&gt;' does not end the element. That state is not modelled,
    /// so such a body is refused rather than framed wrongly.
    /// </summary>
    [Fact]
    public void ScriptDataEscapedBody_IsRefusedRatherThanMisframed() =>
        Assert.Empty(Templates.Csp.Escaped.InlineScriptCspHashes);

    /// <summary>A script that opens and closes inside a conditional is still static.</summary>
    [Fact]
    public void ScriptInsideAConditionalBranch_IsHashed()
    {
        const string body = "\n    var inBranch = 1;\n  ";

        Assert.Contains("<script>" + body + "</script>", Templates.Csp.Branch.ToHtmlString(Flag: true), StringComparison.Ordinal);
        Assert.Equal(Sha256Source(body), Assert.Single(Templates.Csp.Branch.InlineScriptCspHashes));
    }

    /// <summary>
    /// The one that happens: a script commented out during debugging. The bytes
    /// are static and spell a script element, but the browser reads them as
    /// comment text and never executes them — so hashing them adds a `script-src`
    /// entry authorizing that exact source for a script the page does not ship.
    /// </summary>
    [Fact]
    public void CommentedOutScript_IsNotHashed() =>
        Assert.Empty(Templates.Csp.Commented.InlineScriptCspHashes);

    /// <summary>
    /// Inside a raw-text or RCDATA element the content is text, not markup, so
    /// '&lt;script&gt;' there is characters on the page. Same over-authorization
    /// as the comment case.
    /// </summary>
    [Fact]
    public void ScriptInsideRawTextElements_IsNotHashed() =>
        Assert.Empty(Templates.Csp.Rawtext.InlineScriptCspHashes);

    /// <summary>And the third place a '&lt;' starts nothing: an attribute value.</summary>
    [Fact]
    public void ScriptInsideAnAttributeValue_IsNotHashed() =>
        Assert.Empty(Templates.Csp.Attribute.InlineScriptCspHashes);

    /// <summary>
    /// The lockstep, and the test that would have caught all three of the above.
    /// Everything else in this file compares a constant against an expectation
    /// written by hand; this renders the template, hands the output to an HTML
    /// parser that is not the generator's scanner, and checks the two against
    /// each other.
    /// </summary>
    /// <remarks>
    /// **Every emitted hash must be a script the parser found** — that direction
    /// is unconditional, because a hash with no matching script is a `script-src`
    /// entry authorizing bytes the page does not serve. All three defects this
    /// branch fixes are exactly that.
    ///
    /// <para>
    /// The other direction is conditional. A script whose body carries dynamic
    /// output cannot have a build-time hash at all, so `dynamic` and `unquoted`
    /// legitimately serve a script with nothing authorizing it — under a hash-only
    /// CSP those pages simply do not work, which is a page-level fact and is the
    /// consuming app's lockstep to assert. Here it is asserted only for the
    /// fixtures whose scripts are all static, flagged below, so the subset check
    /// above cannot pass by emitting nothing.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(HashedTemplates))]
    public void EmittedHashes_AreHashesOfScriptsTheBrowserWouldActuallyRun(
        string name, string html, string[] emitted, bool allScriptsAreStatic)
    {
        IHtmlDocument document = new HtmlParser().ParseDocument(html);

        var served = document.QuerySelectorAll("script")
            .Where(IsExecutableInline)
            .Select(s => Sha256Source(s.TextContent.Replace("\r\n", "\n").Replace("\r", "\n")))
            .ToList();

        foreach (string hash in emitted)
        {
            Assert.True(
                served.Contains(hash),
                $"{name}: emitted {hash}, which matches no executable inline script in the rendered output.");
        }

        if (allScriptsAreStatic)
        {
            Assert.Equal(
                served.OrderBy(h => h, StringComparer.Ordinal),
                emitted.OrderBy(h => h, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// Every Csp/* fixture, rendered. Params exist only to be filled — the point
    /// is the served bytes, not the values in them. The last column says whether
    /// every executable inline script the fixture serves is build-time static,
    /// and so whether the emitted set must be complete as well as sound.
    /// </summary>
    public static TheoryData<string, string, string[], bool> HashedTemplates() => new()
    {
        { "inline", Templates.Csp.Inline.ToHtmlString(Nonce: "r4nd0m"), Templates.Csp.Inline.InlineScriptCspHashes, true },
        { "crlf", Templates.Csp.Crlf.ToHtmlString(), Templates.Csp.Crlf.InlineScriptCspHashes, true },
        { "two", Templates.Csp.Two.ToHtmlString(), Templates.Csp.Two.InlineScriptCspHashes, true },
        { "closetag", Templates.Csp.Closetag.ToHtmlString(), Templates.Csp.Closetag.InlineScriptCspHashes, true },
        { "json", Templates.Csp.Json.ToHtmlString(), Templates.Csp.Json.InlineScriptCspHashes, true },
        { "branch-true", Templates.Csp.Branch.ToHtmlString(Flag: true), Templates.Csp.Branch.InlineScriptCspHashes, true },
        { "commented", Templates.Csp.Commented.ToHtmlString(), Templates.Csp.Commented.InlineScriptCspHashes, true },
        { "rawtext", Templates.Csp.Rawtext.ToHtmlString(), Templates.Csp.Rawtext.InlineScriptCspHashes, true },
        { "attribute", Templates.Csp.Attribute.ToHtmlString(), Templates.Csp.Attribute.InlineScriptCspHashes, true },
        { "legacytypes", Templates.Csp.Legacytypes.ToHtmlString(), Templates.Csp.Legacytypes.InlineScriptCspHashes, true },

        // Dynamic bodies: sound but deliberately incomplete, per the remarks.
        { "dynamic", Templates.Csp.Dynamic.ToHtmlString(Value: "1"), Templates.Csp.Dynamic.InlineScriptCspHashes, false },
        { "unquoted", Templates.Csp.Unquoted.ToHtmlString(Value: "1"), Templates.Csp.Unquoted.InlineScriptCspHashes, false },
        { "escaped", Templates.Csp.Escaped.ToHtmlString(), Templates.Csp.Escaped.InlineScriptCspHashes, false },
    };

    /// <summary>
    /// What CSP hashes: an inline script (no src) the browser would execute. The
    /// type test mirrors IsExecutableScript in the generator, but reads the
    /// attribute off a parsed DOM rather than off scanned text.
    /// </summary>
    private static bool IsExecutableInline(IElement script)
    {
        if (!string.IsNullOrEmpty(script.GetAttribute("src")) || script.TextContent.Length == 0)
        {
            return false;
        }

        string? type = script.GetAttribute("type")?.Trim();
        if (type is null)
        {
            return true;
        }

        int semicolon = type.IndexOf(';');
        if (semicolon >= 0)
        {
            type = type[..semicolon].Trim();
        }

        return type.Length == 0
            || type.Equals("module", StringComparison.OrdinalIgnoreCase)
            || JavaScriptMimeTypes.Contains(type);
    }

    /// <summary>
    /// A second, independent transcription of the HTML standard's JavaScript
    /// MIME type set. Deliberately not shared with the generator's copy — the
    /// lockstep is only worth running if the two sides can disagree. What pins
    /// the behaviour is LegacyJavaScriptMimeTypes_AreStillHashed above, whose
    /// expectations are hand-written bodies rather than a list.
    /// </summary>
    private static readonly HashSet<string> JavaScriptMimeTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/ecmascript",
            "application/javascript",
            "application/x-ecmascript",
            "application/x-javascript",
            "text/ecmascript",
            "text/javascript",
            "text/javascript1.0",
            "text/javascript1.1",
            "text/javascript1.2",
            "text/javascript1.3",
            "text/javascript1.4",
            "text/javascript1.5",
            "text/jscript",
            "text/livescript",
            "text/x-ecmascript",
            "text/x-javascript",
        };

    /// <summary>Everything between each open tag's '&gt;' and its '&lt;/script'.</summary>
    private static List<string> RenderedScriptBodies(string html)
    {
        List<string> bodies = [];
        int at = 0;
        while ((at = html.IndexOf("<script", at, StringComparison.Ordinal)) >= 0)
        {
            int start = html.IndexOf('>', at) + 1;
            int end = html.IndexOf("</script", start, StringComparison.Ordinal);
            bodies.Add(html.Substring(start, end - start));
            at = end;
        }

        return bodies;
    }

    /// <summary>Independent recomputation: a different API from the generator's.</summary>
    private static string Sha256Source(string body) =>
        "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
}
