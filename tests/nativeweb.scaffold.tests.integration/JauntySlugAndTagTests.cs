using System.Net;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// Spec 022 on the database path: the slug rules the in-memory store proves in
/// <c>ArticleStoreSlugTests</c>, proved again where uniqueness is a unique index
/// and a refusal starts life as a <c>23505</c>; and the tag store's round trip,
/// since tags persist on this path from 022.
/// </summary>
/// <remarks>
/// Every arranged article row uses its id as its slug, which <see
/// cref="SampleTestDatabase.InsertAsync(string, string, string?, string)"/>
/// fixes, so a test that needs a taken slug takes an id. Titles are unique per
/// test for the reason <see cref="JauntyStoreTests"/> gives.
/// </remarks>
public sealed class JauntySlugAndTagTests
{
    private static string Unique() => $"jq-{Guid.NewGuid():N}";

    private static string Key() => Guid.NewGuid().ToString("n")[..8];

    private static Task<HttpResponseMessage> CreateArticleAsync(HttpClient client, string title, string slug) =>
        Forms.PostAsync(
            client,
            "/admin/articles/new",
            "/admin/articles",
            new("title", title),
            new("slug", slug),
            new("author", "xunit"),
            new("body", "b"),
            new("published", "on"),
            new("viewCount", "0"));

    private static Task<HttpResponseMessage> EditArticleAsync(HttpClient client, string id, string title, string slug) =>
        Forms.PostAsync(
            client,
            $"/admin/articles/{id}",
            $"/admin/articles/{id}",
            new("title", title),
            new("slug", slug),
            new("author", "xunit"),
            new("body", "b"),
            new("viewCount", "0"));

    [Fact]
    public async Task Create_WithABlankSlug_DerivesItFromTheTitle()
    {
        string title = Unique();
        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await CreateArticleAsync(client, title, "");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(title, await SampleTestDatabase.SlugByTitleAsync(title));
    }

    [Fact]
    public async Task Create_WhoseDerivedSlugIsTaken_GetsTheNextSuffix()
    {
        string taken = Key();
        await SampleTestDatabase.InsertAsync(taken, Unique(), null, "b");
        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await CreateArticleAsync(client, taken, "");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(taken + "-2", await SampleTestDatabase.SlugByTitleAsync(taken));
    }

    [Fact]
    public async Task Create_WithATypedSlugThatIsTaken_ReRendersTheFormAndWritesNothing()
    {
        string taken = Key();
        string title = Unique();
        await SampleTestDatabase.InsertAsync(taken, Unique(), null, "b");
        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await CreateArticleAsync(client, title, taken);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Slug &#x27;{taken}&#x27; is already used.", body, StringComparison.Ordinal);
        Assert.Contains($"value=\"{title}\"", body, StringComparison.Ordinal);
        Assert.Equal(0, await SampleTestDatabase.CountByTitleAsync(title));
    }

    [Fact]
    public async Task Create_WithAnInvalidSlug_IsRefusedBeforeTheDatabase()
    {
        string title = Unique();
        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await CreateArticleAsync(client, title, "Not A Slug");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Slug may contain only lowercase letters, digits and single hyphens.", body, StringComparison.Ordinal);
        Assert.Equal(0, await SampleTestDatabase.CountByTitleAsync(title));
    }

    [Fact]
    public async Task Update_WithABlankSlug_KeepsTheRowsSlugWhenTheTitleChanges()
    {
        string id = Key();
        string newTitle = Unique();
        await SampleTestDatabase.InsertAsync(id, Unique(), null, "b");
        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await EditArticleAsync(client, id, newTitle, "");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(id, await SampleTestDatabase.SlugByTitleAsync(newTitle));
    }

    [Fact]
    public async Task Update_WithATypedSlug_ChangesIt()
    {
        string id = Key();
        string title = Unique();
        string wanted = Key();
        await SampleTestDatabase.InsertAsync(id, title, null, "b");
        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await EditArticleAsync(client, id, title, wanted);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(wanted, await SampleTestDatabase.SlugByTitleAsync(title));
    }

    [Fact]
    public async Task Update_ToAnotherRowsSlug_ReRendersTheFormAndChangesNothing()
    {
        string id = Key();
        string other = Key();
        string title = Unique();
        await SampleTestDatabase.InsertAsync(id, title, null, "b");
        await SampleTestDatabase.InsertAsync(other, Unique(), null, "b");
        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await EditArticleAsync(client, id, title, other);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Slug &#x27;{other}&#x27; is already used.", body, StringComparison.Ordinal);
        Assert.Contains("<h1>Edit Article</h1>", body, StringComparison.Ordinal);
        Assert.Equal(id, await SampleTestDatabase.SlugByTitleAsync(title));
    }

    [Fact]
    public async Task List_ShowsTheSlugColumn()
    {
        // Created through the form rather than inserted, so the slug differs
        // from the id the store mints and the id column cannot satisfy this.
        string slug = "slug-" + Key();
        string title = Unique();
        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        try
        {
            using HttpResponseMessage created = await CreateArticleAsync(client, title, slug);
            using HttpResponseMessage response = await client.GetAsync(new Uri($"/admin/articles?q={title}", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Found, created.StatusCode);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"<td>{slug}</td>", body, StringComparison.Ordinal);
        }
        finally
        {
            await SampleTestDatabase.DeleteBySlugAsync(slug);
        }
    }

    [Fact]
    public async Task Tags_RoundTripThroughPostgres_CreateEditListDelete()
    {
        string name = Unique();
        string renamed = Unique();
        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage created = await Forms.PostAsync(
            client, "/admin/tags/new", "/admin/tags", new("name", name), new("color", "#123456"));
        Assert.Equal(HttpStatusCode.Found, created.StatusCode);
        string location = created.Headers.Location?.ToString() ?? "";
        string key = location[(location.LastIndexOf('/') + 1)..];
        Assert.Equal(8, key.Length);
        Assert.Equal("#123456", await SampleTestDatabase.TagColorAsync(key));

        using HttpResponseMessage edited = await Forms.PostAsync(
            client, $"/admin/tags/{key}", $"/admin/tags/{key}", new("name", renamed), new("color", "#abcdef"));
        Assert.Equal(HttpStatusCode.Found, edited.StatusCode);
        Assert.Equal("#abcdef", await SampleTestDatabase.TagColorAsync(key));

        using HttpResponseMessage listed = await client.GetAsync(new Uri($"/admin/tags?q={renamed}", UriKind.Relative));
        string list = await listed.Content.ReadAsStringAsync();
        Assert.Contains(renamed, list, StringComparison.Ordinal);
        Assert.DoesNotContain(name, list, StringComparison.Ordinal);

        using HttpResponseMessage deleted = await Forms.PostAsync(client, "/admin/tags", $"/admin/tags/{key}/delete");
        Assert.Equal(HttpStatusCode.Found, deleted.StatusCode);
        Assert.Null(await SampleTestDatabase.TagColorAsync(key));
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task Tags_SortByNameWithTheKeyAsTiebreak_InBothDirections(string dir)
    {
        string name = Unique();
        using var factory = new SampleJauntyFactory();
        using HttpClient client = await factory.CreateSignedInAsync();
        var keys = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            using HttpResponseMessage created = await Forms.PostAsync(
                client, "/admin/tags/new", "/admin/tags", new("name", name), new("color", ""));
            string location = created.Headers.Location?.ToString() ?? "";
            keys.Add(location[(location.LastIndexOf('/') + 1)..]);
        }

        try
        {
            keys.Sort(StringComparer.Ordinal);
            if (dir == "desc")
            {
                keys.Reverse();
            }

            using HttpResponseMessage response = await client.GetAsync(
                new Uri($"/admin/tags?q={name}&sort=Name&dir={dir}", UriKind.Relative));
            string page = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            int[] positions = [.. keys.Select(k => page.IndexOf(k, StringComparison.Ordinal))];
            Assert.All(positions, p => Assert.True(p >= 0));
            Assert.Equal(positions.Order(), positions);
        }
        finally
        {
            foreach (string key in keys)
            {
                using HttpResponseMessage deleted = await Forms.PostAsync(client, "/admin/tags", $"/admin/tags/{key}/delete");
            }
        }
    }
}
