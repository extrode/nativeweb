using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

using HostProgram = NativeWeb.Permissions.Tests.Integration.PublicReadHost.Program;

namespace NativeWeb.Permissions.Tests.Integration;

internal sealed class PublicReadFactory : WebApplicationFactory<HostProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");

    internal HttpClient CreateFollowingNothing() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}

/// <summary>
/// R9 as a served request. <c>Note</c> carries <c>[Auth(Role = "admin",
/// PublicRead = true)]</c> and <c>[Permissions]</c>, so its list is open and
/// everything else is not.
/// </summary>
/// <remarks>
/// Its own host rather than <c>apps/sample</c>: <c>PublicRead</c> is decided at
/// compile time and no model in the demo declares it, so proving it there would
/// mean opening the demo's admin to satisfy a test.
/// </remarks>
public sealed class PublicReadTests
{
    [Fact]
    public async Task AnAnonymousVisitor_IsServedTheList()
    {
        using var factory = new PublicReadFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin/notes", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Readable by anyone", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheListCarriesNoControls_ForAnAnonymousVisitor()
    {
        using var factory = new PublicReadFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin/notes", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("/admin/notes/new\">New</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Edit</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/delete\" data-nws-confirm=", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/admin/notes/new")]
    [InlineData("/admin/notes/1")]
    public async Task TheWriteRoutes_AreUntouchedByPublicRead(string path)
    {
        using var factory = new PublicReadFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ASignedInSubjectWithTheRoleAndNoGrant_IsStillRefusedTheCreateForm()
    {
        using var factory = new PublicReadFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/admin/notes/new", UriKind.Relative));
        request.Headers.Add(HostProgram.SubjectHeader, "someone");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ASignedInSubjectHoldingAdd_ReachesTheCreateForm()
    {
        using var factory = new PublicReadFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        IPermissionStore store = factory.Services.GetService(typeof(IPermissionStore)) as IPermissionStore
            ?? throw new InvalidOperationException("The host registered no IPermissionStore.");

        await store.EnsureSubjectAsync("author", default);
        await store.GrantToSubjectAsync("author", "publicread:note.add", default);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/admin/notes/new", UriKind.Relative));
        request.Headers.Add(HostProgram.SubjectHeader, "author");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
