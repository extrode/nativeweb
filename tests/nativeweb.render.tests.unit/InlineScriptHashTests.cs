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
