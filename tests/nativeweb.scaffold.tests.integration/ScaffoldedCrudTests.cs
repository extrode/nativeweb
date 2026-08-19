using System.Net;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// The generated CRUD endpoints, driven the way a browser drives them.
/// </summary>
/// <remarks>
/// Nothing in <c>apps/sample</c> writes a route, a handler, a form or a view. Every
/// URL these tests hit was emitted from the two attributes on
/// <c>apps/sample/Models.cs</c>, so a failure here is a generator defect.
/// </remarks>
public sealed class ScaffoldedCrudTests
{
    private static async Task<string> BodyAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task List_ShowsEverySeededRowAndItsColumns()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string html = await BodyAsync(client, "/admin/articles");

        Assert.Contains("Lean Native Web", html, StringComparison.Ordinal);
        Assert.Contains("Binding forms under NativeAOT", html, StringComparison.Ordinal);
        Assert.Contains("Scaffolding without reflection", html, StringComparison.Ordinal);

        // Columns come from [ScaffoldList], and its Label override wins.
        Assert.Contains("<th>Views</th>", html, StringComparison.Ordinal);
        Assert.Contains("<th>Created</th>", html, StringComparison.Ordinal);
        Assert.Contains("<th>Title</th>", html, StringComparison.Ordinal);

        // Body carries no [ScaffoldList], so it is not a column.
        Assert.DoesNotContain("<th>Body</th>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_FiltersOnTheSearchableProperties()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string html = await BodyAsync(client, "/admin/articles?q=NativeAOT");

        Assert.Contains("Binding forms under NativeAOT", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Lean Native Web", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditForm_HasAnInputForEveryEditablePropertyAndNoneForTheRest()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string html = await BodyAsync(client, "/admin/articles/a1");

        Assert.Contains("name=\"title\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"author\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"published\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"viewCount\"", html, StringComparison.Ordinal);

        // [ScaffoldMultiline] is a textarea, not an input.
        Assert.Contains("<textarea id=\"body\" name=\"body\"", html, StringComparison.Ordinal);

        // The key and the read-only stamp have no input at all. This is the
        // mass-assignment control, observed from outside.
        Assert.DoesNotContain("name=\"id\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"createdAt\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_AddsARowAndRedirectsToTheList()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/articles/new",
            "/admin/articles",
            new("title", "Written by a test"),
            new("author", "xunit"),
            new("body", "hello"),
            new("published", "on"),
            new("viewCount", "3"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/admin/articles", response.Headers.Location?.ToString());

        string html = await BodyAsync(client, "/admin/articles");
        Assert.Contains("Written by a test", html, StringComparison.Ordinal);
        Assert.Contains("xunit", html, StringComparison.Ordinal);
    }

    // The property the form record exists for: a field nobody declared cannot be
    // bound, because no code reads it. Posting id and createdAt is not rejected --
    // it is simply not seen.
    [Fact]
    public async Task Create_IgnoresFieldsTheFormRecordDoesNotDeclare()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/articles/new",
            "/admin/articles",
            new("title", "Mass assignment attempt"),
            new("body", "x"),
            new("id", "hacked"),
            new("createdAt", "1999-01-01T00:00"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        // The chosen key was not honoured: /admin/articles/hacked does not exist.
        using HttpResponseMessage hijacked = await client.GetAsync(new Uri("/admin/articles/hacked", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, hijacked.StatusCode);

        // Nor the chosen timestamp: the store stamped it, so it is this year.
        string html = await BodyAsync(client, "/admin/articles?q=Mass%20assignment");
        Assert.Contains("Mass assignment attempt", html, StringComparison.Ordinal);
        Assert.DoesNotContain("1999-01-01", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_ChangesTheRowAndPreservesTheReadOnlyStamp()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/articles/a1",
            "/admin/articles/a1",
            new("title", "Renamed"),
            new("author", "Sy"),
            new("body", "still here"),
            new("published", "on"),
            new("viewCount", "1420"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        string html = await BodyAsync(client, "/admin/articles");
        Assert.Contains("Renamed", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Lean Native Web", html, StringComparison.Ordinal);

        // CreatedAt has no input, so an update cannot touch it.
        Assert.Contains("2026-03-04 09:30", html, StringComparison.Ordinal);
    }

    // An unticked checkbox submits nothing, so omitting the field has to clear it.
    [Fact]
    public async Task Update_ClearsABooleanWhenTheCheckboxIsOmitted()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/articles/a1",
            "/admin/articles/a1",
            new("title", "Lean Native Web"),
            new("author", "Sy"),
            new("body", "b"),
            new("viewCount", "1420"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        string html = await BodyAsync(client, "/admin/articles?q=Lean");
        Assert.Contains("<td>No</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_OnAMissingRowIsNotFound()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/articles/new",
            "/admin/articles/nope",
            new("title", "t"),
            new("body", "b"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesTheRow()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/articles",
            "/admin/articles/a2/delete");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        string html = await BodyAsync(client, "/admin/articles");
        Assert.DoesNotContain("Binding forms under NativeAOT", html, StringComparison.Ordinal);
        Assert.Contains("Lean Native Web", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_WithABlankRequiredFieldRedisplaysTheFormWithTheError()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await Forms.PostAsync(
            client,
            "/admin/articles/new",
            "/admin/articles",
            new("title", "   "),
            new("body", "b"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Title is required.", html, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);

        // Nullable Author is not required, so it raises no error of its own.
        Assert.DoesNotContain("Author is required.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminIndex_LinksEveryScaffoldedModel()
    {
        using var factory = new SampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        string html = await BodyAsync(client, "/admin");

        Assert.Contains("href=\"/admin/articles\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/admin/tags\"", html, StringComparison.Ordinal);
    }
}
