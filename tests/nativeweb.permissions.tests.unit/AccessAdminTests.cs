using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Xunit;

namespace NativeWeb.Permissions.Tests.Unit;

/// <summary>
/// The access screens, driven as routes rather than as methods, because what R13
/// and R14 promise is about requests: which posts are refused, what a dead grant
/// renders as, and where the screens live.
/// </summary>
public sealed class AccessAdminTests
{
    private static readonly Permission View = new("sample:article.view");
    private static readonly Permission Change = new("sample:article.change");

    [Fact]
    public async Task TheScreensSitUnderTheScaffoldRoot()
    {
        await using Harness harness = await Harness.StartAsync();

        using HttpResponseMessage response = await harness.Client.GetAsync(new Uri("/admin/access/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task APermissionOutsideTheCatalog_IsRefusedBeforeItReachesTheStore()
    {
        await using Harness harness = await Harness.StartAsync();
        long group = await harness.Store.CreateGroupAsync("editors", default);

        using HttpResponseMessage response = await harness.PostAsync(
            $"/admin/access/groups/{group}/grants", ("permission", "sample:article.publish"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await harness.Store.GroupGrantsAsync(group, default));
    }

    [Fact]
    public async Task ADeclaredPermission_IsGrantedToTheGroup()
    {
        await using Harness harness = await Harness.StartAsync();
        long group = await harness.Store.CreateGroupAsync("editors", default);

        using HttpResponseMessage response = await harness.PostAsync(
            $"/admin/access/groups/{group}/grants", ("permission", Change.Name));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal([Change.Name], await harness.Store.GroupGrantsAsync(group, default));
    }

    [Fact]
    public async Task ADeclaredPermission_IsGrantedToTheSubject()
    {
        await using Harness harness = await Harness.StartAsync();

        using HttpResponseMessage response = await harness.PostAsync(
            "/admin/access/subjects/sub-1/grants", ("permission", View.Name));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal([View.Name], await harness.Store.SubjectGrantsAsync("sub-1", default));
    }

    // R14's second half. The declaration is gone, the row is not, and the only
    // thing the screen offers for it is the way to remove it.
    [Fact]
    public async Task AGrantNamingAnUndeclaredPermission_IsListedAsUndeclaredAndOnlyRevocable()
    {
        await using Harness harness = await Harness.StartAsync();
        await harness.Store.EnsureSubjectAsync("sub-1", default);
        await harness.Store.GrantToSubjectAsync("sub-1", "sample:article.publish", default);

        string html = await harness.GetAsync("/admin/access/subjects/sub-1");

        Assert.Contains("sample:article.publish", html, StringComparison.Ordinal);
        Assert.Contains("undeclared", html, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<option value=\"sample:article.publish\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUndeclaredGrant_CanStillBeRevoked()
    {
        await using Harness harness = await Harness.StartAsync();
        await harness.Store.EnsureSubjectAsync("sub-1", default);
        await harness.Store.GrantToSubjectAsync("sub-1", "sample:article.publish", default);

        using HttpResponseMessage response = await harness.PostAsync(
            "/admin/access/subjects/sub-1/grants/revoke", ("permission", "sample:article.publish"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(await harness.Store.SubjectGrantsAsync("sub-1", default));
    }

    [Fact]
    public async Task AGroupName_IsEncodedOnRender()
    {
        await using Harness harness = await Harness.StartAsync();
        await harness.Store.CreateGroupAsync("<script>alert(1)</script>", default);

        string html = await harness.GetAsync("/admin/access/");

        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASubjectName_IsEncodedOnRender()
    {
        await using Harness harness = await Harness.StartAsync();
        await harness.Store.EnsureSubjectAsync("<img src=x>", default);

        string html = await harness.GetAsync("/admin/access/");

        Assert.DoesNotContain("<img src=x>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondGroupOfTheSameName_IsAConflictRatherThanASecondGroup()
    {
        await using Harness harness = await Harness.StartAsync();
        await harness.Store.CreateGroupAsync("editors", default);

        using HttpResponseMessage response = await harness.PostAsync("/admin/access/groups", ("name", "editors"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await harness.Store.GroupsAsync(default));
    }

    [Fact]
    public async Task AMemberAddedThroughTheScreen_IsAMember()
    {
        await using Harness harness = await Harness.StartAsync();
        long group = await harness.Store.CreateGroupAsync("editors", default);

        using HttpResponseMessage response = await harness.PostAsync(
            $"/admin/access/groups/{group}/members", ("subject", "sub-1"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(["sub-1"], await harness.Store.MembersAsync(group, default));
    }

    [Fact]
    public async Task RemovingAMember_LeavesTheGroupAndTheSubject()
    {
        await using Harness harness = await Harness.StartAsync();
        long group = await harness.Store.CreateGroupAsync("editors", default);
        await harness.Store.EnsureSubjectAsync("sub-1", default);
        await harness.Store.AddMemberAsync("sub-1", group, default);

        await harness.PostAsync($"/admin/access/groups/{group}/members/remove", ("subject", "sub-1"));

        Assert.Empty(await harness.Store.MembersAsync(group, default));
        Assert.Single(await harness.Store.GroupsAsync(default));
    }

    [Fact]
    public async Task AGroupThatDoesNotExist_IsNotFound()
    {
        await using Harness harness = await Harness.StartAsync();

        using HttpResponseMessage response =
            await harness.Client.GetAsync(new Uri("/admin/access/groups/404", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static readonly Regex TokenPattern = new(
        """<input type="hidden" name="(__[^"]+)" value="([^"]*)">""",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    /// <summary>The chrome's antiforgery field, rendered the way a host renders it.</summary>
    private static string Token(HttpContext http)
    {
        IAntiforgery antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);

        return $"""<input type="hidden" name="{tokens.FormFieldName}" value="{tokens.RequestToken}">""";
    }

    [Theory]
    [InlineData("members", "subject", "sub-1")]
    [InlineData("grants", "permission", "sample:article.change")]
    public async Task APostNamingAGroupThatDoesNotExist_IsAConflict(string leaf, string field, string value)
    {
        await using Harness harness = await Harness.StartAsync();

        using HttpResponseMessage response = await harness.PostAsync(
            $"/admin/access/groups/{NoSuchGroup}/{leaf}", (field, value));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private const long NoSuchGroup = 4_294_967_296;

    private sealed class Harness : IAsyncDisposable
    {
        private WebApplication _app = null!;

        internal InMemoryPermissionStore Store { get; private set; } = null!;

        internal HttpClient Client { get; private set; } = null!;

        internal static async Task<Harness> StartAsync()
        {
            var harness = new Harness { Store = new InMemoryPermissionStore() };

            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IPermissionStore>(harness.Store);
            builder.Services.AddPermissions([View, Change]);
            builder.Services.AddAntiforgery();

            harness._app = builder.Build();
            harness._app.UseAntiforgery();

            // The chrome the host would pass from its generated ScaffoldLayout. A
            // pair of delegates here rather than the real thing, because the real
            // thing is generated into an application assembly and this tier has none.
            // The token is real, not a placeholder: the screens' posts carry
            // IAntiforgeryMetadata, so a stub value is refused before it arrives.
            harness._app.MapAccessAdmin(
                "/admin",
                (http, title, body) => Results.Content($"<h1>{title}</h1>{body}", "text/html"),
                Token);

            await harness._app.StartAsync();

            harness.Client = harness._app.GetTestClient();
            harness.Client.BaseAddress = new Uri("https://localhost/");

            return harness;
        }

        internal async Task<string> GetAsync(string path)
        {
            using HttpResponseMessage response = await Client.GetAsync(new Uri(path, UriKind.Relative));
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        internal async Task<HttpResponseMessage> PostAsync(string path, params (string Key, string Value)[] fields)
        {
            (KeyValuePair<string, string> field, string cookie) = await TokenPairAsync();

            using var body = new FormUrlEncodedContent(
                [field, .. fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))]);

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
            {
                Content = body,
            };

            request.Headers.Add("Cookie", cookie);

            return await Client.SendAsync(request);
        }

        // The pair, taken from a rendered page the way a browser takes it. The cookie
        // half is carried by hand because the TestServer client keeps no cookie jar,
        // and antiforgery refuses a request that brings only the field.
        private async Task<(KeyValuePair<string, string> Field, string Cookie)> TokenPairAsync()
        {
            using HttpResponseMessage page =
                await Client.GetAsync(new Uri("/admin/access/", UriKind.Relative));
            page.EnsureSuccessStatusCode();

            Match match = TokenPattern.Match(await page.Content.ReadAsStringAsync());

            if (!match.Success)
            {
                throw new InvalidOperationException("The access index rendered no antiforgery field.");
            }

            string cookie = string.Join(
                "; ",
                page.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));

            return (new KeyValuePair<string, string>(match.Groups[1].Value, match.Groups[2].Value), cookie);
        }

        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
