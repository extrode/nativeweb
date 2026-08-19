using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// The guards the generator attaches, none of which the consumer wrote.
/// </summary>
/// <remarks>
/// Measured in brainstorm 001 sec.12: binding runs before the endpoint-filter
/// pipeline, so neither of these can be a filter. The antiforgery check needs
/// metadata the platform only attaches to <c>[FromForm]</c> handlers, and the
/// content-type check has to live inside <c>BindAsync</c> itself. Both are emitted,
/// so neither can be forgotten -- which is the whole argument for generating them.
/// </remarks>
public sealed class ScaffoldedGuardTests
{
    [Fact]
    public async Task ListOnAnAuthedModel_RedirectsAnAnonymousVisitorToSignIn()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin/articles", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/signin", response.Headers.Location?.ToString(), StringComparison.Ordinal);
        Assert.Contains("ReturnUrl=%2Fadmin%2Farticles", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    // Tag carries no [Auth], so its routes are open. The difference between the two
    // models is the attribute and nothing else.
    [Fact]
    public async Task ListOnAModelWithNoAuthAttribute_IsOpen()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin/tags", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostWithNoAntiforgeryToken_IsRejected()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("name", "no token")]);
        using HttpResponseMessage response = await client.PostAsync(new Uri("/admin/tags", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // And the row was not written.
        using HttpResponseMessage list = await client.GetAsync(new Uri("/admin/tags", UriKind.Relative));
        Assert.DoesNotContain("no token", await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteWithNoAntiforgeryToken_IsRejected()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using var content = new FormUrlEncodedContent([]);
        using HttpResponseMessage response = await client.PostAsync(new Uri("/admin/tags/aot/delete", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using HttpResponseMessage list = await client.GetAsync(new Uri("/admin/tags", UriKind.Relative));
        Assert.Contains("NativeAOT", await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // A valid token with a JSON body: the antiforgery guard passes, so this reaches
    // the binder, which is the only thing that can reject it.
    [Fact]
    public async Task PostWithAValidTokenButANonFormBody_IsUnsupportedMediaType()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        KeyValuePair<string, string> token = await Forms.AntiforgeryFieldAsync(client, "/admin/tags/new");

        using var content = new StringContent("{\"name\":\"json\"}", Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.Add("RequestVerificationToken", token.Value);

        using HttpResponseMessage response = await client.PostAsync(new Uri("/admin/tags", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task EveryGeneratedFormCarriesAnAntiforgeryField()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        foreach (string path in new[] { "/admin/articles", "/admin/articles/new", "/admin/articles/a1", "/admin/tags" })
        {
            KeyValuePair<string, string> token = await Forms.AntiforgeryFieldAsync(client, path);
            Assert.False(string.IsNullOrEmpty(token.Value), $"No antiforgery value on {path}.");
        }
    }
}
