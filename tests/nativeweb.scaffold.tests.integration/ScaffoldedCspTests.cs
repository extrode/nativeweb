using System.Net.Http;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// The scaffolded admin renders under the framework's Content-Security-Policy
/// (spec 015 T8).
/// </summary>
/// <remarks>
/// <para>
/// A scaffolded page is self-contained on purpose: its stylesheet is an inline
/// <c>&lt;style&gt;</c> and its delete confirmation is inline script, so a host
/// gets a working admin the moment it maps scaffolding, with no asset pipeline to
/// wire first. That design and a nonce CSP meet head-on, and until 2026-08-31
/// nothing here noticed because <c>apps/sample</c> served no CSP at all.
/// </para>
/// <para>
/// What noticed when it did was the Playwright tier, with "the delete form did not
/// raise a confirm dialog at all" — four e2e facts for a browser-only failure that
/// no unit or integration test could see. These are the cheaper facts that fail
/// first: the nonce is on the inline elements, and the confirmation is an
/// attribute rather than an <c>onsubmit</c> handler no CSP will run.
/// </para>
/// </remarks>
public sealed class ScaffoldedCspTests
{
    private static async Task<(string Html, string Csp)> AdminAsync()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin/articles", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        return (
            await response.Content.ReadAsStringAsync(),
            string.Join(' ', response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task TheInlineStyleAndScriptCarryTheRequestsNonce()
    {
        (string html, string csp) = await AdminAsync();

        int start = csp.IndexOf("'nonce-", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No nonce in the policy: {csp}");
        string nonce = csp[(start + 7)..csp.IndexOf('\'', start + 7)];

        // Decoded, because the attribute is HTML-encoded on the way out and a
        // base64 nonce routinely carries '+' and '=' -- which HtmlEncoder.Default
        // writes as entities. A browser decodes them before matching the policy,
        // so the encoded form is right and comparing raw bytes would be the test
        // asserting the wrong thing.
        string decoded = System.Net.WebUtility.HtmlDecode(html);

        Assert.Contains($"<style nonce=\"{nonce}\">", decoded, StringComparison.Ordinal);
        Assert.Contains($"<script nonce=\"{nonce}\">", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDeleteConfirmationIsAnAttributeAndNotAnInlineHandler()
    {
        (string html, string csp) = await AdminAsync();

        Assert.Contains("data-nws-confirm=\"Delete this record?\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("onsubmit=", html, StringComparison.Ordinal);

        // The policy that makes the above necessary: no 'unsafe-hashes', so an
        // inline handler would not run even if one were emitted.
        Assert.DoesNotContain("unsafe-hashes", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
    }
}
