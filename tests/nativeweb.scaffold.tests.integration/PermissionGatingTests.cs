using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NativeWeb.Permissions;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// <c>apps/sample</c> with the permission store replaced by one the test fills.
/// </summary>
/// <remarks>
/// The sample seeds its whole catalog to <c>demo</c> at startup, which is right for
/// a demo and useless for asserting on what a subject without a grant sees. The
/// replacement is registered in <c>ConfigureTestServices</c>, which runs after the
/// application's own registrations, so it wins.
/// </remarks>
internal sealed class GrantedSampleFactory(params string[] granted)
    : WebApplicationFactory<NativeWeb.Sample.Program>
{
    internal const string Subject = "demo";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Sample", string.Empty);

        builder.ConfigureTestServices(services =>
        {
            var store = new InMemoryPermissionStore();
            store.EnsureSubjectAsync(Subject, default).AsTask().GetAwaiter().GetResult();

            foreach (string permission in granted)
            {
                store.GrantToSubjectAsync(Subject, permission, default).AsTask().GetAwaiter().GetResult();
            }

            services.RemoveAll<IPermissionStore>();
            services.AddSingleton<IPermissionStore>(store);
        });
    }

    internal async Task<HttpClient> CreateSignedInAsync()
    {
        HttpClient client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage response = await client.GetAsync(new Uri("/signin", UriKind.Relative));

        if (response.StatusCode != HttpStatusCode.Found)
        {
            throw new InvalidOperationException($"Demo sign-in did not redirect; got {(int)response.StatusCode}.");
        }

        return client;
    }
}

/// <summary>
/// R12: the generated admin draws only the controls the signed-in subject may use,
/// and the routes behind them refuse independently of the markup.
/// </summary>
public sealed class PermissionGatingTests
{
    private const string View = "sample:article.view";
    private const string Add = "sample:article.add";
    private const string Change = "sample:article.change";
    private const string Delete = "sample:article.delete";

    private const string NewButton = "/admin/articles/new\">New</a>";
    private const string EditLink = ">Edit</a>";
    private const string DeleteForm = "/delete\" data-nws-confirm=";

    [Fact]
    public async Task TheWholeCatalog_DrawsEveryControl()
    {
        string html = await ListAsync(View, Add, Change, Delete);

        Assert.Contains(NewButton, html, StringComparison.Ordinal);
        Assert.Contains(EditLink, html, StringComparison.Ordinal);
        Assert.Contains(DeleteForm, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ViewAlone_DrawsTheRowsAndNoneOfTheControls()
    {
        string html = await ListAsync(View);

        Assert.Contains("<table", html, StringComparison.Ordinal);
        Assert.DoesNotContain(NewButton, html, StringComparison.Ordinal);
        Assert.DoesNotContain(EditLink, html, StringComparison.Ordinal);
        Assert.DoesNotContain(DeleteForm, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeWithoutDelete_DrawsEditAndNotDelete()
    {
        string html = await ListAsync(View, Change);

        Assert.Contains(EditLink, html, StringComparison.Ordinal);
        Assert.DoesNotContain(DeleteForm, html, StringComparison.Ordinal);
        Assert.DoesNotContain(NewButton, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddWithoutChange_DrawsNewAndNotEdit()
    {
        string html = await ListAsync(View, Add);

        Assert.Contains(NewButton, html, StringComparison.Ordinal);
        Assert.DoesNotContain(EditLink, html, StringComparison.Ordinal);
        Assert.DoesNotContain(DeleteForm, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutView_TheListRouteRefuses()
    {
        using var factory = new GrantedSampleFactory(Add, Change, Delete);
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin/articles", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WithoutView_TheAdminIndexOmitsTheLink()
    {
        using var factory = new GrantedSampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("href=\"/admin/articles\"", html, StringComparison.Ordinal);
        // Tag declares no permissions, so its link is decided by the role alone and
        // must not disappear along with the gated one.
        Assert.Contains("href=\"/admin/tags\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithView_TheAdminIndexCarriesTheLink()
    {
        using var factory = new GrantedSampleFactory(View);
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Contains("href=\"/admin/articles\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNewRoute_RefusesWithoutAdd_EvenThoughTheListRendered()
    {
        using var factory = new GrantedSampleFactory(View, Change, Delete);
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin/articles/new", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheDeleteRoute_RefusesWithoutDelete()
    {
        using var factory = new GrantedSampleFactory(View, Add, Change);
        using HttpClient client = await factory.CreateSignedInAsync();

        // With a real token, because antiforgery runs ahead of the endpoint filter:
        // posting without one returns 400 and proves nothing about the permission.
        // The token is taken from the create form, which this subject may open.
        using HttpResponseMessage response =
            await Forms.PostAsync(client, "/admin/articles/new", "/admin/articles/1/delete");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnUngatedModel_IsUntouchedByAnyOfThis()
    {
        using var factory = new GrantedSampleFactory();
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin/tags", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/admin/tags/new\">New</a>", html, StringComparison.Ordinal);
    }

    private static async Task<string> ListAsync(params string[] granted)
    {
        using var factory = new GrantedSampleFactory(granted);
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin/articles", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }
}
