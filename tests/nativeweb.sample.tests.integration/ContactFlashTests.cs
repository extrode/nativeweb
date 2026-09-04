using System.Net;

using NativeWeb.Web;

using Xunit;

namespace NativeWeb.Sample.Tests.Integration;

/// <summary>
/// The contact form's post-redirect-get, and the framework-owned banner partials it
/// renders through (spec 029 R9).
/// </summary>
/// <remarks>
/// <para>
/// <c>shared/banner-ok.html</c> and <c>shared/banner-error.html</c> moved into
/// <c>libs/nativeweb.web/templates/</c> and are delivered to this application by
/// <c>NativeWeb.Web.targets</c>. Nothing else in the tree renders one, so without
/// this the delivery of two of the four framework templates is proved only by the
/// build succeeding, which it does whether or not the markup ever reaches a response.
/// </para>
/// <para>
/// Two responses, deliberately. The 302 is where the flash is armed and the GET is
/// where it is spent, and a client that follows the redirect on the test's behalf
/// hands back only the second, which is how a flash that was set twice or never
/// expired would go unnoticed.
/// </para>
/// </remarks>
public sealed class ContactFlashTests
{
    [Fact]
    public async Task A_sent_message_redirects_and_the_redirected_page_carries_the_banner()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowserThatStopsAtRedirects();

        Dictionary<string, string> carried = await Pages.CarriedFieldsAsync(client, "/contact");

        using HttpResponseMessage posted = await Pages.PostAsync(
            client,
            carried,
            "/contact",
            ("name", "Ada"),
            ("email", "ada@example.test"),
            ("message", "Hello from the reference application."));

        Assert.Equal(HttpStatusCode.Found, posted.StatusCode);
        Assert.Equal("/contact", posted.Headers.Location?.ToString());
        Assert.Contains(
            SetCookies(posted),
            value => value.StartsWith(Flash.CookieName + "=ok%3a", StringComparison.OrdinalIgnoreCase));

        using HttpResponseMessage redirected = await client.GetAsync(new Uri("/contact", UriKind.Relative));
        string body = await redirected.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, redirected.StatusCode);
        Assert.Contains("<div class=\"banner banner--ok\" role=\"status\">", body, StringComparison.Ordinal);
        Assert.Contains("ada@example.test", body, StringComparison.Ordinal);
        Assert.DoesNotContain("banner--err", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Read-once: the response that renders the message also expires the cookie that
    /// carried it, so a reload shows the form and nothing else.
    /// </summary>
    [Fact]
    public async Task The_page_that_renders_the_flash_expires_it_in_the_same_response()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowserThatStopsAtRedirects();

        Dictionary<string, string> carried = await Pages.CarriedFieldsAsync(client, "/contact");

        using HttpResponseMessage posted = await Pages.PostAsync(
            client,
            carried,
            "/contact",
            ("name", "Ada"),
            ("email", "ada@example.test"),
            ("message", "Hello."));

        Assert.Equal(HttpStatusCode.Found, posted.StatusCode);

        using HttpResponseMessage redirected = await client.GetAsync(new Uri("/contact", UriKind.Relative));

        Assert.Contains("banner--ok", await redirected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(
            SetCookies(redirected),
            value => value.StartsWith(Flash.CookieName + "=;", StringComparison.OrdinalIgnoreCase)
                && value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

        using HttpResponseMessage reloaded = await client.GetAsync(new Uri("/contact", UriKind.Relative));

        Assert.DoesNotContain("banner--ok", await reloaded.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A page nobody flashed to renders no banner, so the assertions above are reading
    /// the flash rather than markup the layout emits unconditionally.
    /// </summary>
    [Fact]
    public async Task A_page_reached_without_a_flash_renders_no_banner()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        string body = await client.GetStringAsync(new Uri("/contact", UriKind.Relative));

        Assert.DoesNotContain("class=\"banner", body, StringComparison.Ordinal);
    }

    private static string[] SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? [.. values] : [];
}
