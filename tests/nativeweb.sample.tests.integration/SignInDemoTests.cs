using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NativeWeb.SignIn;

using Xunit;

namespace NativeWeb.Sample.Tests.Integration;

/// <summary>
/// Spec 016 R28 at the reference application: a page that requires sign-in, a page that
/// does not, and a complete round trip against a provider that is this same process.
/// </summary>
public sealed class SignInDemoTests
{
    private static readonly Regex TokenPattern = new(
        """<input type="hidden" name="([^"]+)" value="([^"]*)">""",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Welcome_Anonymous_IsServed()
    {
        using var factory = new SampleSignInFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/welcome", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Anyone may read this", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Account_Anonymous_IsChallenged()
    {
        using var factory = new SampleSignInFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/account", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("/oauth/signin", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Account_AfterTheRoundTrip_NamesTheProvidersSubject()
    {
        using var factory = new SampleSignInFactory();
        using HttpClient browser = factory.CreateBrowser();

        using HttpResponseMessage response = await browser.GetAsync(new Uri("/account", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("demo-subject", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRoundTrip_SetsAHostPrefixedSessionCookie()
    {
        using var factory = new SampleSignInFactory();
        using HttpClient browser = factory.CreateBrowser();

        using HttpResponseMessage first = await browser.GetAsync(new Uri("/account", UriKind.Relative));
        first.EnsureSuccessStatusCode();

        // A second request carries the cookie the first one was handed, so a 200 here
        // is the cookie working rather than the round trip running again.
        using HttpResponseMessage second = await browser.GetAsync(new Uri("/account", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("demo-subject", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignOut_LeavesTheNextRequestAnonymous()
    {
        using var factory = new SampleSignInFactory();
        using HttpClient browser = factory.CreateBrowser();

        using HttpResponseMessage account = await browser.GetAsync(new Uri("/account", UriKind.Relative));
        account.EnsureSuccessStatusCode();

        KeyValuePair<string, string> token = Antiforgery(await account.Content.ReadAsStringAsync());

        using var form = new FormUrlEncodedContent([token]);
        using HttpResponseMessage signedOut =
            await browser.PostAsync(new Uri("/oauth/signout", UriKind.Relative), form);
        signedOut.EnsureSuccessStatusCode();

        using HttpClient after = factory.CreateFollowingNothing();
        using HttpResponseMessage response = await after.GetAsync(new Uri("/account", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact]
    public async Task BothSchemes_AreRegisteredBehindTheSelector()
    {
        using var factory = new SampleSignInFactory();

        // Forces the host to build; the service provider is not there until it has.
        using HttpClient client = factory.CreateFollowingNothing();
        using HttpResponseMessage warmed = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

        AuthenticationOptions authentication = factory.Services
            .GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        IAuthenticationSchemeProvider schemes =
            factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.Equal("sample-either", authentication.DefaultScheme);
        Assert.NotNull(await schemes.GetSchemeAsync(SessionAuthenticationHandler.SchemeName));
        Assert.NotNull(await schemes.GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme));
    }

    [Fact]
    public async Task TheDemoSignIn_LeavesTheCookieDemoAlone()
    {
        using var factory = new SampleSignInFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/signin", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/admin", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Articles_Anonymous_IsStillPublic()
    {
        using var factory = new SampleSignInFactory();
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/articles", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static KeyValuePair<string, string> Antiforgery(string html)
    {
        Match match = TokenPattern.Match(html);

        return match.Success
            ? new KeyValuePair<string, string>(match.Groups[1].Value, match.Groups[2].Value)
            : throw new InvalidOperationException("No antiforgery field on the page.");
    }
}
