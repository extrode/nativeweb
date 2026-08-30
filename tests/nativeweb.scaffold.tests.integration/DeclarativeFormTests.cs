using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// The declarative binder (spec 014) through a real host, on a <b>hand-written</b>
/// route.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ScaffoldedGuardTests"/> proves the same guards on routes the
/// scaffolding generator emits whole. What is unproven until here is the case 014
/// exists for: an ordinary <c>MapPost</c> that someone wrote themselves, taking a
/// <c>[Form]</c> record as a parameter, with the 415 and the antiforgery metadata
/// arriving from the attribute rather than from the author's memory.
/// </para>
/// <para>
/// Every status here was measured going the other way first (brainstorm 001 §12): a
/// route in this shape without the generated guards answered <b>200 OK to a POST
/// with no token</b> and <b>500 to a JSON body</b>. Those are the two numbers these
/// tests exist to keep from coming back.
/// </para>
/// </remarks>
public sealed class DeclarativeFormTests
{
    private const string Feed = "/articles";

    [Fact]
    public async Task A_form_post_binds_every_declared_field()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await Forms.PostAsync(
            client, Feed, Feed, new KeyValuePair<string, string>("query", "Lean"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Lean Native Web", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding forms under NativeAOT", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The <c>[FormField]</c> name, not the member name. If the generator fell back
    /// to the convention this would bind nothing and the count would not change.
    /// </summary>
    /// <remarks>
    /// Stated against the unfiltered feed rather than against a literal, because a
    /// literal here is fixture arithmetic: seeding one more article would fail this
    /// test without breaking anything it is about. The claim is "the cap bound and
    /// took effect", which needs an unfiltered feed longer than the cap to mean
    /// anything at all — so that is asserted too rather than assumed.
    /// </remarks>
    [Fact]
    public async Task A_renamed_field_binds_under_its_wire_name()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        int unfiltered = await FeedCountAsync(client);
        Assert.True(unfiltered > 1, $"The feed seeds {unfiltered} articles; a cap of 1 proves nothing.");

        using HttpResponseMessage response = await Forms.PostAsync(
            client, Feed, Feed, new KeyValuePair<string, string>("max", "1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, ArticleCount(await response.Content.ReadAsStringAsync()));
    }

    /// <summary>
    /// R4, and the reason the feature is called declarative binding rather than form
    /// binding: the member is named <c>Take</c>, but only <c>max</c> is on the wire,
    /// so a field called <c>take</c> is not a field at all.
    /// </summary>
    /// <remarks>
    /// "Unchanged from the unfiltered feed" is the claim, so the unfiltered feed is
    /// what it is measured against. The fact above proves a cap that IS declared
    /// changes the count, so an unchanged count here is the field being ignored
    /// rather than the cap never working.
    /// </remarks>
    [Fact]
    public async Task An_undeclared_field_is_not_read()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        int unfiltered = await FeedCountAsync(client);

        using HttpResponseMessage response = await Forms.PostAsync(
            client, Feed, Feed, new KeyValuePair<string, string>("take", "1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(unfiltered, ArticleCount(await response.Content.ReadAsStringAsync()));
    }

    private static async Task<int> FeedCountAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri(Feed, UriKind.Relative));
        return ArticleCount(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The guard whose absence is silent. Measured at 200 OK before the metadata was
    /// attached; anything but 400 here means it came off again.
    /// </summary>
    [Fact]
    public async Task A_post_with_no_antiforgery_token_is_rejected()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using var content = new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("query", "Lean")]);
        using HttpResponseMessage response = await client.PostAsync(new Uri(Feed, UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A valid token with a JSON body: the antiforgery check passes, so this reaches
    /// the binder, which is the only thing that can reject it. 415, not the 500 a
    /// hand-written <c>BindAsync</c> gives.
    /// </summary>
    [Fact]
    public async Task A_json_post_carrying_a_valid_token_is_415()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        KeyValuePair<string, string> token = await Forms.AntiforgeryFieldAsync(client, Feed);

        using var content = new StringContent("""{"query":"Lean"}""", Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Feed, UriKind.Relative))
        {
            Content = content,
        };
        request.Headers.Add("RequestVerificationToken", token.Value);

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    /// <summary>
    /// A body with no content type at all takes the same path. This is the case a
    /// <c>HasFormContentType</c> check written as an endpoint filter would miss,
    /// because the read has already happened by the time a filter runs.
    /// </summary>
    [Fact]
    public async Task A_post_with_no_content_type_is_415()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        KeyValuePair<string, string> token = await Forms.AntiforgeryFieldAsync(client, Feed);

        using var content = new ByteArrayContent([1, 2, 3]);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Feed, UriKind.Relative))
        {
            Content = content,
        };
        request.Headers.Add("RequestVerificationToken", token.Value);

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    /// <summary>The GET renders the form the binder expects, token and all.</summary>
    [Fact]
    public async Task The_feed_renders_a_form_the_binder_can_read()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri(Feed, UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("""<form class="articles__filter" method="post" action="/articles">""", html, StringComparison.Ordinal);
        Assert.Contains("""name="query" """, html, StringComparison.Ordinal);
        Assert.Contains("""name="max" """, html, StringComparison.Ordinal);
    }

    private static int ArticleCount(string html)
    {
        int count = 0;
        int at = html.IndexOf("articles__item", StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = html.IndexOf("articles__item", at + 1, StringComparison.Ordinal);
        }

        return count;
    }
}
