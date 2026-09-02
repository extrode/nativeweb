using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

using Field = System.Collections.Generic.KeyValuePair<string, string>;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// Spec 023's acceptance criteria through the real entry point, on both stores:
/// the article-tag relation edited from either side, status and its public
/// consequences, the publication date's ordering, the category reference, the
/// three refusals, and the read-only view count.
/// </summary>
/// <remarks>
/// Every test creates its own rows through the admin forms, so the same code
/// runs over the seeded in-memory store and over <c>nativeweb_sample_test</c>,
/// where fifty thousand rows and every other test's leftovers share the tables.
/// On the SQL side a row that must lead a public page gets a publication date
/// far in the future, and is deleted at the end so it leads nothing after.
/// </remarks>
public sealed class AdminRelationsTests
{
    private static readonly Regex EditLink = new(
        "href=\"/admin/([a-z]+)/([^\"]+)\"",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    private static readonly Regex Cell = new(
        "<t([hd])[^>]*>(.*?)</t[hd]>",
        RegexOptions.Singleline,
        TimeSpan.FromSeconds(1));

    private static readonly Regex Markup = new("<[^>]+>", RegexOptions.None, TimeSpan.FromSeconds(1));

    public static TheoryData<bool> Stores => new() { false, true };

    private static string Unique() => $"rel-{Guid.NewGuid():N}";

    // --- the relation, from both sides (R1, R2) --------------------------------

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task TickingTwoTags_ListsTheArticleOnBothTagPagesAndBothTagsOnItsPage(bool sql)
    {
        using Site site = await Site.OpenAsync(sql);
        string title = Unique();
        string id = await site.CreateArticleAsync(title, "Published");
        try
        {
            using HttpResponseMessage saved = await site.EditArticleAsync(
                id, title, "Published", new Field("tags", "aot"), new Field("tags", "htmx"));
            Assert.Equal(HttpStatusCode.Found, saved.StatusCode);

            Assert.Contains(title, await site.BodyAsync($"/tags/aot"), StringComparison.Ordinal);
            Assert.Contains(title, await site.BodyAsync($"/tags/htmx"), StringComparison.Ordinal);

            string page = await site.BodyAsync($"/articles/{title}");
            Assert.Contains("href=\"/tags/aot\"", page, StringComparison.Ordinal);
            Assert.Contains("href=\"/tags/htmx\"", page, StringComparison.Ordinal);

            string form = await site.AdminBodyAsync($"/admin/articles/{id}");
            Assert.Contains("name=\"tags\" value=\"aot\" checked", form, StringComparison.Ordinal);
            Assert.Contains("name=\"tags\" value=\"htmx\" checked", form, StringComparison.Ordinal);
            Assert.Contains("name=\"tags\" value=\"postgres\">", form, StringComparison.Ordinal);

            // Unticking is the same post with one key fewer: the set is replaced,
            // not appended to.
            using HttpResponseMessage unticked = await site.EditArticleAsync(id, title, "Published", new Field("tags", "aot"));
            Assert.Equal(HttpStatusCode.Found, unticked.StatusCode);

            string after = await site.AdminBodyAsync($"/admin/articles/{id}");
            Assert.Contains("name=\"tags\" value=\"aot\" checked", after, StringComparison.Ordinal);
            Assert.Contains("name=\"tags\" value=\"htmx\">", after, StringComparison.Ordinal);
            Assert.DoesNotContain(title, await site.BodyAsync("/tags/htmx"), StringComparison.Ordinal);
        }
        finally
        {
            await site.DeleteArticleAsync(id);
        }
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task DeselectingTheArticleOnTheTagForm_UnticksItOnTheArticleFormAndDropsItFromTheTagPage(bool sql)
    {
        using Site site = await Site.OpenAsync(sql);
        string title = Unique();
        string id = await site.CreateArticleAsync(title, "Published", new Field("tags", "htmx"), new Field("tags", "aot"));
        try
        {
            string tagForm = await site.AdminBodyAsync("/admin/tags/htmx");
            Assert.Contains($"<option value=\"{id}\" selected>", tagForm, StringComparison.Ordinal);

            // Every other article the tag holds stays held: the post carries them all
            // but this one, the way a browser posts a multi-select.
            var kept = SelectedOptions(tagForm)
                .Where(held => held != id)
                .Select(held => new KeyValuePair<string, string>("articles", held))
                .ToList();
            using HttpResponseMessage saved = await site.PostAsync(
                "/admin/tags/htmx",
                "/admin/tags/htmx",
                [new Field("name", "HTMX"), new Field("color", "#7aa2f7"), .. kept]);
            Assert.Equal(HttpStatusCode.Found, saved.StatusCode);

            string articleForm = await site.AdminBodyAsync($"/admin/articles/{id}");
            Assert.Contains("name=\"tags\" value=\"htmx\">", articleForm, StringComparison.Ordinal);
            Assert.Contains("name=\"tags\" value=\"aot\" checked", articleForm, StringComparison.Ordinal);
            Assert.DoesNotContain(title, await site.BodyAsync("/tags/htmx"), StringComparison.Ordinal);
            Assert.Contains(title, await site.BodyAsync("/tags/aot"), StringComparison.Ordinal);
        }
        finally
        {
            await site.DeleteArticleAsync(id);
        }
    }

    // --- status (R3) ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Archiving_AnswersTheUnknownSlugs404AndLeavesEveryPublicDocument(bool sql)
    {
        using Site site = await Site.OpenAsync(sql);
        string title = Unique();
        string id = await site.CreateArticleAsync(title, "Published");
        try
        {
            Assert.Contains(title, await site.BodyAsync("/"), StringComparison.Ordinal);

            using HttpResponseMessage saved = await site.EditArticleAsync(id, title, "Archived");
            Assert.Equal(HttpStatusCode.Found, saved.StatusCode);
            Assert.Contains("<option value=\"Archived\" selected>", await site.AdminBodyAsync($"/admin/articles/{id}"), StringComparison.Ordinal);

            (HttpResponseMessage archived, string archivedBody) = await site.GetAsync($"/articles/{title}");
            (HttpResponseMessage unknown, string unknownBody) = await site.GetAsync($"/articles/{Unique()}");
            Assert.Equal(HttpStatusCode.NotFound, archived.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal(unknownBody, archivedBody);

            foreach (string document in new[] { "/", "/feed", "/sitemap.xml", "/articles.json", "/articles.csv" })
            {
                Assert.DoesNotContain(title, await site.BodyAsync(document), StringComparison.Ordinal);
            }
        }
        finally
        {
            await site.DeleteArticleAsync(id);
        }
    }

    // --- the publication date (R4) ----------------------------------------------

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task ALaterPublicationDate_LeadsTheHomePageOverANewerRow(bool sql)
    {
        using Site site = await Site.OpenAsync(sql);
        string older = Unique();
        string newer = Unique();
        string olderId = await site.CreateArticleAsync(older, "Published", new Field("publishedAt", "2999-01-01"));
        string newerId = await site.CreateArticleAsync(newer, "Published", new Field("publishedAt", "2999-01-02"));
        try
        {
            string before = await site.BodyAsync("/");
            Assert.True(before.IndexOf(newer, StringComparison.Ordinal) < before.IndexOf(older, StringComparison.Ordinal));

            using HttpResponseMessage saved = await site.EditArticleAsync(
                olderId, older, "Published", new Field("publishedAt", "2999-01-03"));
            Assert.Equal(HttpStatusCode.Found, saved.StatusCode);

            string after = await site.BodyAsync("/");
            int olderAt = after.IndexOf(older, StringComparison.Ordinal);
            Assert.True(olderAt >= 0, "the re-dated row left the home page");
            Assert.True(olderAt < after.IndexOf(newer, StringComparison.Ordinal));
        }
        finally
        {
            await site.DeleteArticleAsync(olderId);
            await site.DeleteArticleAsync(newerId);
        }
    }

    // --- the category reference (R5) ----------------------------------------------

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task PickingACategory_ShowsItsNameInTheRow_AndDeletingItLeavesNone(bool sql)
    {
        using Site site = await Site.OpenAsync(sql);
        string title = Unique();
        string category = Unique();
        string categoryKey = await site.CreateCategoryAsync(category);
        string id = await site.CreateArticleAsync(title, "Draft", new Field("categoryKey", categoryKey));
        try
        {
            Assert.Equal(category, await site.ListCellAsync("articles", id, "Category", title));
            Assert.Contains(
                $"<option value=\"{categoryKey}\" selected>{category}</option>",
                await site.AdminBodyAsync($"/admin/articles/{id}"),
                StringComparison.Ordinal);

            using HttpResponseMessage deleted = await site.PostAsync(
                $"/admin/categories/{categoryKey}", $"/admin/categories/{categoryKey}/delete");
            Assert.Equal(HttpStatusCode.Found, deleted.StatusCode);

            Assert.Equal("", await site.ListCellAsync("articles", id, "Category", title));
            string form = await site.AdminBodyAsync($"/admin/articles/{id}");
            Assert.Contains("<option value=\"\">(none)</option>", form, StringComparison.Ordinal);
            Assert.DoesNotContain(categoryKey, form, StringComparison.Ordinal);
        }
        finally
        {
            await site.DeleteArticleAsync(id);
        }
    }

    // --- the refusals (R7) ----------------------------------------------------------

    public static TheoryData<bool, string, string, string> Refusals
    {
        get
        {
            var data = new TheoryData<bool, string, string, string>();
            foreach (bool sql in new[] { false, true })
            {
                data.Add(sql, "status", "Deleted", "Status must be one of Draft, Published, Archived.");
                data.Add(sql, "tags", "no-such-tag", "Tags has a choice that does not exist.");
                data.Add(sql, "publishedAt", "2026-02-30", "Published must be a date.");
                data.Add(sql, "categoryKey", "no-such-category", "Category has a choice that does not exist.");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task AnInvalidPost_ReRendersTheFormWithTheMessageAndLeavesTheRowUnchanged(
        bool sql, string field, string value, string message)
    {
        using Site site = await Site.OpenAsync(sql);
        string title = Unique();
        string id = await site.CreateArticleAsync(title, "Draft", new Field("tags", "aot"));
        try
        {
            using HttpResponseMessage response = await site.EditArticleAsync(
                id, "Renamed by a refused post", field == "status" ? null : "Published", new Field(field, value));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // The form comes back with what was typed. A status that is no member
            // has no option to come back as; every other refused value is re-rendered.
            string html = await response.Content.ReadAsStringAsync();
            Assert.Contains(message, html, StringComparison.Ordinal);
            Assert.Contains("value=\"Renamed by a refused post\"", html, StringComparison.Ordinal);
            if (field != "status")
            {
                Assert.Contains($"value=\"{value}\"", html, StringComparison.Ordinal);
            }

            string form = await site.AdminBodyAsync($"/admin/articles/{id}");
            Assert.Contains($"value=\"{title}\"", form, StringComparison.Ordinal);
            Assert.Contains("<option value=\"Draft\" selected>", form, StringComparison.Ordinal);
            Assert.Contains("name=\"tags\" value=\"aot\" checked", form, StringComparison.Ordinal);
        }
        finally
        {
            await site.DeleteArticleAsync(id);
        }
    }

    // --- the read-only view count (R8) ---------------------------------------------

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task SavingAFormOpenedBeforeThreeVisits_KeepsAllFortyThreeViews(bool sql)
    {
        using Site site = await Site.OpenAsync(sql);
        string title = Unique();
        string id = await site.CreateArticleAsync(title, "Published");
        try
        {
            await site.VisitAsync($"/articles/{title}", 40);
            Assert.Equal("40", await site.ListCellAsync("articles", id, "Views", title));

            KeyValuePair<string, string> token = await Forms.AntiforgeryFieldAsync(site.Admin, $"/admin/articles/{id}");
            await site.VisitAsync($"/articles/{title}", 3);

            using var content = new FormUrlEncodedContent(
                [token, new Field("title", title), new Field("author", "xunit"), new Field("body", "b"), new Field("status", "Published")]);
            using HttpResponseMessage saved = await site.Admin.PostAsync(new Uri($"/admin/articles/{id}", UriKind.Relative), content);
            Assert.Equal(HttpStatusCode.Found, saved.StatusCode);

            Assert.Equal("43", await site.ListCellAsync("articles", id, "Views", title));
        }
        finally
        {
            await site.DeleteArticleAsync(id);
        }
    }

    // --- the migration's backfill, on the SQL side only (R3) -------------------------

    [Fact]
    public async Task ARowPublishedBeforeTheChange_IsPublishedAfterIt_AndAnUnpublishedOneIsDraft()
    {
        using Site site = await Site.OpenAsync(sql: true);
        string live = Unique();
        string draft = Unique();
        await SampleTestDatabase.InsertAsync(live, live, "xunit", "b", published: true, createdAt: DateTime.UtcNow);
        await SampleTestDatabase.InsertAsync(draft, draft, "xunit", "b", published: false, createdAt: DateTime.UtcNow);
        try
        {
            Assert.Contains("<option value=\"Published\" selected>", await site.AdminBodyAsync($"/admin/articles/{live}"), StringComparison.Ordinal);
            Assert.Contains("<option value=\"Draft\" selected>", await site.AdminBodyAsync($"/admin/articles/{draft}"), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, (await site.GetAsync($"/articles/{live}")).Response.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await site.GetAsync($"/articles/{draft}")).Response.StatusCode);
        }
        finally
        {
            await site.DeleteArticleAsync(live);
            await site.DeleteArticleAsync(draft);
        }
    }

    private static List<string> SelectedOptions(string html) =>
        Regex.Matches(html, "<option value=\"([^\"]+)\" selected>", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(m => m.Groups[1].Value)
            .ToList();

    // One site, either store, with a signed-in admin and an anonymous visitor.
    private sealed class Site : IDisposable
    {
        private readonly WebApplicationFactory<NativeWeb.Sample.Program> _factory;

        private Site(WebApplicationFactory<NativeWeb.Sample.Program> factory, HttpClient admin, HttpClient visitor, HttpClient reader)
        {
            _factory = factory;
            Admin = admin;
            Visitor = visitor;
            Reader = reader;
            Reader.DefaultRequestHeaders.Authorization = new("Bearer", "uncached");
        }

        public HttpClient Admin { get; }

        public HttpClient Visitor { get; }

        public HttpClient Reader { get; }

        public static async Task<Site> OpenAsync(bool sql)
        {
            if (sql)
            {
                var jaunty = new SampleJauntyFactory();
                return new Site(jaunty, await jaunty.CreateSignedInAsync(), jaunty.CreateFollowingNothing(), jaunty.CreateFollowingNothing());
            }

            var memory = new SampleFactory();
            return new Site(memory, await memory.CreateSignedInAsync(), memory.CreateFollowingNothing(), memory.CreateFollowingNothing());
        }

        public void Dispose()
        {
            Admin.Dispose();
            Visitor.Dispose();
            Reader.Dispose();
            _factory.Dispose();
        }

        public Task<HttpResponseMessage> PostAsync(string tokenPath, string action, params KeyValuePair<string, string>[] fields) =>
            Forms.PostAsync(Admin, tokenPath, action, fields);

        // Public pages are read through a client that sends an Authorization
        // header: the public output cache holds a page for sixty seconds, and the
        // default policy underneath NativeWeb's never caches an authorised request,
        // so this reads what the store holds now rather than what it held before
        // the edit under test. The visitor that counts views carries nothing.
        public async Task<(HttpResponseMessage Response, string Body)> GetAsync(string path)
        {
            HttpResponseMessage response = await Reader.GetAsync(new Uri(path, UriKind.Relative));
            return (response, await response.Content.ReadAsStringAsync());
        }

        public async Task<string> BodyAsync(string path)
        {
            (HttpResponseMessage response, string body) = await GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return body;
        }

        public async Task<string> AdminBodyAsync(string path)
        {
            using HttpResponseMessage response = await Admin.GetAsync(new Uri(path, UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }

        // The title doubles as the slug, so the public page is /articles/{title}.
        // Dated far ahead unless the caller dates it, so that on the SQL side the
        // row leads the home page and its tag pages over every other test's rows.
        public async Task<string> CreateArticleAsync(string title, string status, params KeyValuePair<string, string>[] more)
        {
            KeyValuePair<string, string>[] dated = more.Any(f => f.Key == "publishedAt")
                ? []
                : [new Field("publishedAt", "2999-05-05")];
            using HttpResponseMessage response = await PostAsync(
                "/admin/articles/new",
                "/admin/articles",
                [new Field("title", title), new Field("slug", title), new Field("author", "xunit"), new Field("body", "b"), new Field("status", status), .. dated, .. more]);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);

            return await KeyOfAsync("articles", title);
        }

        public Task<HttpResponseMessage> EditArticleAsync(string id, string title, string? status, params KeyValuePair<string, string>[] more) =>
            PostAsync(
                $"/admin/articles/{id}",
                $"/admin/articles/{id}",
                [new Field("title", title), new Field("author", "xunit"), new Field("body", "b"), .. Status(status), .. more]);

        private static KeyValuePair<string, string>[] Status(string? status) =>
            status is null ? [] : [new Field("status", status)];

        public async Task DeleteArticleAsync(string id)
        {
            using HttpResponseMessage response = await PostAsync("/admin/articles", $"/admin/articles/{id}/delete");
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        }

        public async Task<string> CreateCategoryAsync(string name)
        {
            using HttpResponseMessage response = await PostAsync("/admin/categories/new", "/admin/categories", new Field("name", name));
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);

            return await KeyOfAsync("categories", name);
        }

        public async Task VisitAsync(string path, int times)
        {
            HttpResponseMessage[] responses = await Task.WhenAll(
                Enumerable.Range(0, times).Select(_ => Visitor.GetAsync(new Uri(path, UriKind.Relative))));
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            Array.ForEach(responses, r => r.Dispose());
        }

        // The key of the one row whose search hit carries this text, read off its
        // edit link: the admin generates keys, so a test learns them from the list.
        private async Task<string> KeyOfAsync(string route, string query)
        {
            string list = await AdminBodyAsync($"/admin/{route}?q={Uri.EscapeDataString(query)}");
            Match link = EditLink.Match(list, list.IndexOf(query, StringComparison.Ordinal));
            Assert.True(link.Success && link.Groups[1].Value == route, $"no {route} row for {query}");
            return link.Groups[2].Value;
        }

        public async Task<string> ListCellAsync(string route, string key, string column, string query)
        {
            string list = await AdminBodyAsync($"/admin/{route}?q={Uri.EscapeDataString(query)}");
            int link = list.IndexOf($"href=\"/admin/{route}/{key}\"", StringComparison.Ordinal);
            Assert.True(link >= 0, $"no {route} row for {key}");

            List<string> headers = CellsOf(list[..list.IndexOf("</thead>", StringComparison.Ordinal)]);
            List<string> cells = CellsOf(list[list.LastIndexOf("<tr>", link, StringComparison.Ordinal)..link]);
            return cells[headers.IndexOf(column)];
        }

        private static List<string> CellsOf(string row) =>
            Cell.Matches(row).Select(m => Markup.Replace(m.Groups[2].Value, "").Trim()).ToList();
    }
}
