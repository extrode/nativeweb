using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// Spec 022's pages through a real host, on both stores: the home page, the
/// article page with its tags and its view count, the tag pages, the three
/// kinds of 404, the cache each page does or does not sit in, and the admin
/// slug rules seen from the public side.
/// </summary>
/// <remarks>
/// The sample unit tier states what each reader and each template does. What
/// is unproven until here is the wiring: that the routes exist, that the seam
/// resolves to the store the configuration named, that the output cache holds
/// the home page and never the article page, and that a view counted under
/// fifty concurrent requests is the number the admin then reads.
/// </remarks>
public sealed class PublicSiteTests : IClassFixture<LeakedFeedRowsSweep>
{
    // The view count is read-only since spec 023 made it [ScaffoldReadOnly], so the
    // edit form no longer carries it; the admin list does, in its Views column.
    private static readonly Regex Cell = new(
        "<t([hd])[^>]*>(.*?)</t[hd]>",
        RegexOptions.Singleline,
        TimeSpan.FromSeconds(1));

    private static readonly Regex Tag = new("<[^>]+>", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static async Task<(HttpResponseMessage Response, string Body)> GetAsync(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        return (response, await response.Content.ReadAsStringAsync());
    }

    private static async Task<int> AdminViewCountAsync(HttpClient signedIn, string id, string query = "")
    {
        (HttpResponseMessage response, string list) =
            await GetAsync(signedIn, "/admin/articles?q=" + Uri.EscapeDataString(query));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int link = list.IndexOf($"href=\"/admin/articles/{id}\"", StringComparison.Ordinal);
        Assert.True(link >= 0, $"The admin list has no row for {id}.");
        int rowStart = list.LastIndexOf("<tr>", link, StringComparison.Ordinal);
        int rowEnd = list.IndexOf("</tr>", link, StringComparison.Ordinal);

        List<string> headers = CellsOf(list[..list.IndexOf("</thead>", StringComparison.Ordinal)]);
        int column = headers.IndexOf("Views");
        Assert.True(column >= 0, "The admin list has no Views column.");

        List<string> cells = CellsOf(list[rowStart..rowEnd]);
        return int.Parse(cells[column], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<string> CellsOf(string row) =>
        Cell.Matches(row).Select(m => Tag.Replace(m.Groups[2].Value, "").Trim()).ToList();

    private static async Task GetFiftyTimesAsync(HttpClient client, string path)
    {
        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ => client.GetAsync(new Uri(path, UriKind.Relative))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Array.ForEach(responses, r => r.Dispose());
    }

    private static string TagRow(string slug, string name, int published) =>
        $"<a class=\"tag\" href=\"/tags/{slug}\">{name}</a> "
        + $"<span class=\"tags__count\" title=\"Published articles\">{published}</span>";

    private static string[] HeaderNames(HttpResponseMessage response) =>
        [.. response.Headers.Concat(response.Content.Headers).Select(h => h.Key).Where(k => k != "Date").Order()];

    private static Task<HttpResponseMessage> CreateArticleAsync(HttpClient client, string title, string slug) =>
        Forms.PostAsync(
            client,
            "/admin/articles/new",
            "/admin/articles",
            new("title", title),
            new("slug", slug),
            new("author", "xunit"),
            new("body", "First paragraph.\n\nSecond paragraph."),
            new("status", "Published"));

    // --- the in-memory store, over the seeded demo rows -------------------------

    [Fact]
    public async Task Home_LinksEverySeededPublishedArticleAndNoDraft()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (HttpResponseMessage response, string html) = await GetAsync(client, "/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("href=\"/articles/binding-forms-under-nativeaot\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/articles/lean-native-web\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("scaffolding-without-reflection", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Scaffolding without reflection", html, StringComparison.Ordinal);
        Assert.True(
            html.IndexOf("binding-forms-under-nativeaot", StringComparison.Ordinal)
                < html.IndexOf("lean-native-web", StringComparison.Ordinal),
            "The newer article must come first.");
    }

    [Fact]
    public async Task Detail_RendersTheArticleWithItsTagsAndParagraphs()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (HttpResponseMessage response, string html) = await GetAsync(client, "/articles/lean-native-web");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1 class=\"page__title\">Lean Native Web</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<p>Server-rendered HTML, one native binary, no client build step.</p>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/tags/htmx\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/tags/aot\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/tags/postgres\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_AnUnknownAnUnpublishedAndAnInvalidSlugAreTheSame404()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (HttpResponseMessage unknown, string unknownBody) = await GetAsync(client, "/articles/no-such-article");
        (HttpResponseMessage draft, string draftBody) = await GetAsync(client, "/articles/scaffolding-without-reflection");
        (HttpResponseMessage invalid, string invalidBody) = await GetAsync(client, "/articles/Not%20A%20Slug");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        Assert.Equal(HeaderNames(unknown), HeaderNames(draft));
        Assert.Equal(HeaderNames(unknown), HeaderNames(invalid));
        Assert.Equal(unknownBody, draftBody);
        Assert.Equal(unknownBody, invalidBody);
    }

    [Fact]
    public async Task Detail_CountsOneViewPerRenderUnderFiftyConcurrentRequests()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();
        using HttpClient admin = await factory.CreateSignedInAsync();

        await GetFiftyTimesAsync(client, "/articles/lean-native-web");

        Assert.Equal(1420 + 50, await AdminViewCountAsync(admin, "a1"));
    }

    [Fact]
    public async Task Detail_CountsNothingForA404()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();
        using HttpClient admin = await factory.CreateSignedInAsync();

        (HttpResponseMessage response, _) = await GetAsync(client, "/articles/scaffolding-without-reflection");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await AdminViewCountAsync(admin, "a3"));
    }

    [Fact]
    public async Task Home_IsServedFromTheCacheOnTheSecondRequest()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (HttpResponseMessage first, string firstBody) = await GetAsync(client, "/");
        (HttpResponseMessage second, string secondBody) = await GetAsync(client, "/");

        Assert.StartsWith("public, max-age=", first.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.False(first.Headers.Contains("Age"));
        Assert.True(second.Headers.Contains("Age"), "The second request rendered again instead of reading the cache.");
        Assert.Equal(firstBody, secondBody);
        Assert.False(first.Headers.Contains("Set-Cookie"), "A cached page must not set a cookie.");
    }

    [Fact]
    public async Task Detail_IsNeverMarkedCacheableNorServedFromTheCache()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();
        using HttpClient admin = await factory.CreateSignedInAsync();

        (HttpResponseMessage first, _) = await GetAsync(client, "/articles/binding-forms-under-nativeaot");
        (HttpResponseMessage second, _) = await GetAsync(client, "/articles/binding-forms-under-nativeaot");

        Assert.DoesNotContain("public", first.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
        Assert.False(first.Headers.Contains("Age"));
        Assert.False(second.Headers.Contains("Age"));
        Assert.Equal(388 + 2, await AdminViewCountAsync(admin, "a2"));
    }

    [Fact]
    public async Task Tags_ListEveryTagWithItsPublishedCountAndOneTagListsItsArticles()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (HttpResponseMessage index, string indexHtml) = await GetAsync(client, "/tags");
        (HttpResponseMessage aot, string aotHtml) = await GetAsync(client, "/tags/aot");
        (HttpResponseMessage postgres, string postgresHtml) = await GetAsync(client, "/tags/postgres");
        (HttpResponseMessage missing, _) = await GetAsync(client, "/tags/no-such-tag");

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains(TagRow("aot", "NativeAOT", 1), indexHtml, StringComparison.Ordinal);
        Assert.Contains(TagRow("htmx", "HTMX", 1), indexHtml, StringComparison.Ordinal);
        Assert.Contains(TagRow("postgres", "PostgreSQL", 0), indexHtml, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, aot.StatusCode);
        Assert.Contains("<h1 class=\"page__title\">NativeAOT</h1>", aotHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"/articles/lean-native-web\"", aotHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("binding-forms-under-nativeaot", aotHtml, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, postgres.StatusCode);
        Assert.DoesNotContain("scaffolding-without-reflection", postgresHtml, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Admin_CreateWithABlankSlug_DerivesItFromTheTitleAndThePageAnswers()
    {
        using var factory = new SampleFactory();
        using HttpClient admin = await factory.CreateSignedInAsync();
        using HttpClient visitor = factory.CreateFollowingNothing();

        using HttpResponseMessage created = await CreateArticleAsync(admin, "Hello, World! (Again)", "");
        (HttpResponseMessage page, string html) = await GetAsync(visitor, "/articles/hello-world-again");

        Assert.Equal(HttpStatusCode.Found, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("<h1 class=\"page__title\">Hello, World! (Again)</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<p>First paragraph.</p>", html, StringComparison.Ordinal);
        Assert.Contains("<p>Second paragraph.</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_CreateWithATakenSlug_ReRendersTheFormAndPublishesNothing()
    {
        using var factory = new SampleFactory();
        using HttpClient admin = await factory.CreateSignedInAsync();
        using HttpClient visitor = factory.CreateFollowingNothing();

        using HttpResponseMessage refused = await CreateArticleAsync(admin, "Impostor", "lean-native-web");
        string form = await refused.Content.ReadAsStringAsync();
        (_, string json) = await GetAsync(visitor, "/articles.json");

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("is already used.", form, StringComparison.Ordinal);
        Assert.Contains("name=\"title\"", form, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.DoesNotContain("Impostor", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheJsonListingStillAnswersBesideTheArticlePage()
    {
        using var factory = new SampleFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        (HttpResponseMessage json, string body) = await GetAsync(client, "/articles.json");
        (HttpResponseMessage page, _) = await GetAsync(client, "/articles/lean-native-web");

        Assert.Equal(HttpStatusCode.OK, json.StatusCode);
        Assert.Equal("application/json", json.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"slug\":\"lean-native-web\"", body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
    }

    // --- the JauntyQ store, over rows this test inserts -------------------------

    [Fact]
    public async Task OverPostgres_HomeDetailTagsAndViewCountReadTheDatabase()
    {
        string prefix = $"feed-{Guid.NewGuid():N}"[..16];
        DateTime future = DateTime.UtcNow.AddYears(5);

        try
        {
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-p", $"{prefix} published", "xunit", "Body one.\n\nBody two.", published: true, createdAt: future);
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-d", $"{prefix} draft", "xunit", "body", published: false, createdAt: future.AddMinutes(1));
            await SampleTestDatabase.LinkTagAsync($"{prefix}-p", "aot");
            await SampleTestDatabase.LinkTagAsync($"{prefix}-p", "htmx");
            await SampleTestDatabase.LinkTagAsync($"{prefix}-d", "postgres");

            using var factory = new SampleJauntyFactory();
            using HttpClient client = factory.CreateFollowingNothing();
            using HttpClient admin = await factory.CreateSignedInAsync();

            (HttpResponseMessage home, string homeHtml) = await GetAsync(client, "/");
            (HttpResponseMessage detail, string detailHtml) = await GetAsync(client, $"/articles/{prefix}-p");
            (HttpResponseMessage draft, _) = await GetAsync(client, $"/articles/{prefix}-d");
            (HttpResponseMessage tag, string tagHtml) = await GetAsync(client, "/tags/aot");
            (HttpResponseMessage tags, string tagsHtml) = await GetAsync(client, "/tags");

            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
            Assert.Contains($"href=\"/articles/{prefix}-p\"", homeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain($"{prefix}-d", homeHtml, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Contains($"<h1 class=\"page__title\">{prefix} published</h1>", detailHtml, StringComparison.Ordinal);
            Assert.Contains("<p>Body one.</p>", detailHtml, StringComparison.Ordinal);
            Assert.Contains("<p>Body two.</p>", detailHtml, StringComparison.Ordinal);
            Assert.Contains("href=\"/tags/htmx\"", detailHtml, StringComparison.Ordinal);
            Assert.Contains("href=\"/tags/aot\"", detailHtml, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);
            Assert.Equal(HttpStatusCode.OK, tag.StatusCode);
            Assert.Contains($"href=\"/articles/{prefix}-p\"", tagHtml, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, tags.StatusCode);
            Assert.Contains("href=\"/tags/postgres\"", tagsHtml, StringComparison.Ordinal);

            await GetFiftyTimesAsync(client, $"/articles/{prefix}-p");

            Assert.Equal(51, await AdminViewCountAsync(admin, $"{prefix}-p", prefix));
            Assert.Equal(0, await AdminViewCountAsync(admin, $"{prefix}-d", prefix));
        }
        finally
        {
            await SampleTestDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }

    [Fact]
    public async Task OverPostgres_HomeIsCachedAndTheArticlePageIsNot()
    {
        string prefix = $"feed-{Guid.NewGuid():N}"[..16];

        try
        {
            await SampleTestDatabase.InsertAsync(
                $"{prefix}-p", $"{prefix} published", "xunit", "body", published: true, createdAt: DateTime.UtcNow.AddYears(5));

            using var factory = new SampleJauntyFactory();
            using HttpClient client = factory.CreateFollowingNothing();
            using HttpClient admin = await factory.CreateSignedInAsync();

            (HttpResponseMessage firstHome, string firstBody) = await GetAsync(client, "/");
            (HttpResponseMessage secondHome, string secondBody) = await GetAsync(client, "/");
            (HttpResponseMessage firstDetail, _) = await GetAsync(client, $"/articles/{prefix}-p");
            (HttpResponseMessage secondDetail, _) = await GetAsync(client, $"/articles/{prefix}-p");

            Assert.StartsWith("public, max-age=", firstHome.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
            Assert.False(firstHome.Headers.Contains("Age"));
            Assert.True(secondHome.Headers.Contains("Age"));
            Assert.Equal(firstBody, secondBody);
            Assert.DoesNotContain("public", firstDetail.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
            Assert.False(firstDetail.Headers.Contains("Age"));
            Assert.False(secondDetail.Headers.Contains("Age"));
            Assert.Equal(2, await AdminViewCountAsync(admin, $"{prefix}-p", prefix));
        }
        finally
        {
            await SampleTestDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }

    [Fact]
    public async Task OverPostgres_AdminBlankSlugDerivesAndATakenSlugIsRefused()
    {
        string prefix = $"feed-{Guid.NewGuid():N}"[..16];
        string title = $"{prefix}-typed";

        try
        {
            await SampleTestDatabase.InsertAsync($"{prefix}-p", $"{prefix} taken", "xunit", "b");

            using var factory = new SampleJauntyFactory();
            using HttpClient admin = await factory.CreateSignedInAsync();
            using HttpClient visitor = factory.CreateFollowingNothing();

            using HttpResponseMessage created = await CreateArticleAsync(admin, title, "");
            (HttpResponseMessage page, string html) = await GetAsync(visitor, $"/articles/{title}");
            using HttpResponseMessage refused = await CreateArticleAsync(admin, $"{prefix} impostor", $"{prefix}-p");
            string form = await refused.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Found, created.StatusCode);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains($"<h1 class=\"page__title\">{title}</h1>", html, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
            Assert.Contains("is already used.", form, StringComparison.Ordinal);
            Assert.Equal(0, await SampleTestDatabase.CountByTitleAsync($"{prefix} impostor"));
        }
        finally
        {
            await SampleTestDatabase.DeleteBySlugAsync(title);
            await SampleTestDatabase.DeleteByIdPrefixAsync(prefix);
        }
    }
}
