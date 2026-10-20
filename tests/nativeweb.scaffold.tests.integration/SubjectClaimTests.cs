using System.Net;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NativeWeb.Permissions;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// <c>apps/sample</c> with its subject arriving as <c>sub</c> rather than under the
/// WS-Fed NameIdentifier URI, which is what a JWT bearer scheme that does not map
/// inbound claims produces.
/// </summary>
/// <remarks>
/// The transformation moves the claim rather than adding one, so the default is
/// genuinely absent: an application on such a scheme sees no subject at all, which
/// before spec 025 was an unexplained 403 on every gated route.
/// </remarks>
internal sealed class BareSubSampleFactory(bool configured)
    : WebApplicationFactory<NativeWeb.Sample.Program>
{
    internal const string Subject = "demo";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Sample", string.Empty);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IClaimsTransformation, MoveSubjectToSub>();

            if (configured)
            {
                services.RemoveAll<PermissionSubjectResolver>();
                services.AddSingleton(
                    new PermissionSubjectResolver(new PermissionSubjectOptions { ClaimName = "sub" }));
            }
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

    private sealed class MoveSubjectToSub : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity is not ClaimsIdentity identity)
            {
                return Task.FromResult(principal);
            }

            Claim? subject = identity.FindFirst(ClaimTypes.NameIdentifier);

            if (subject is not null)
            {
                identity.RemoveClaim(subject);
                identity.AddClaim(new Claim("sub", subject.Value));
            }

            return Task.FromResult(principal);
        }
    }
}

public sealed class SubjectClaimTests
{
    private const string NewButton = "/admin/articles/new\">New</a>";
    private const string EditLink = ">Edit</a>";

    [Fact]
    public async Task AHostThatNamesItsClaim_ReachesTheAdminItSeeded()
    {
        string html = await ListAsync(configured: true);

        Assert.Contains("<table", html, StringComparison.Ordinal);
        Assert.Contains(NewButton, html, StringComparison.Ordinal);
        Assert.Contains(EditLink, html, StringComparison.Ordinal);
    }

    // The state spec 025 exists to end: signed in, authenticated, every claim
    // present, and the admin refuses with nothing to say the subject was never
    // found. Asserted so that configuring the claim is provably what fixes it.
    [Fact]
    public async Task AHostThatDoesNot_IsRefusedTheListItSeeded()
    {
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(configured: false));
    }

    [Fact]
    public async Task AHostThatNamesItsClaim_IsNotRefused()
    {
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(configured: true));
    }

    private static async Task<string> ListAsync(bool configured)
    {
        using var factory = new BareSubSampleFactory(configured);
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin/articles", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpStatusCode> StatusAsync(bool configured)
    {
        using var factory = new BareSubSampleFactory(configured);
        using HttpClient client = await factory.CreateSignedInAsync();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/admin/articles", UriKind.Relative));

        return response.StatusCode;
    }
}
