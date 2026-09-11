using System.Net;

using Microsoft.Extensions.DependencyInjection;

using NativeWeb.SignIn.Testing;

using Xunit;

namespace NativeWeb.SignIn.Tests.Integration;

public sealed class RoundTripTests
{
    [Fact]
    public async Task SignIn_CompletesTheRoundTripAndLandsOnTheReturnPath()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());

        using HttpResponseMessage response = await browser.GetAsync("/signin?returnTo=/private");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith(
            "private:", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignIn_CarriesTheProvidersSubjectIntoTheSession()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        host.Provider.Subject = "provider-subject-42";
        using var browser = new Browser(host.Client());

        await browser.GetAsync("/signin?returnTo=/private");
        using HttpResponseMessage response = await browser.GetAsync("/private");

        Assert.Equal(
            "private:provider-subject-42", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignIn_WritesAHostOnlyHttpOnlySecureSameSiteLaxCookie()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());

        await browser.GetAsync("/signin");

        // The session cookie by name: the flow correlator is also a __Host- cookie on this
        // exchange, written when the flow started and deleted when the callback took it.
        string cookie = Assert.Single(
            browser.SetCookies, c => c.StartsWith("__Host-signin=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignIn_NeverPutsAProviderTokenInTheCookie()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());

        await browser.GetAsync("/signin");

        Assert.All(
            browser.SetCookies.Where(c => c.StartsWith("__Host-", StringComparison.Ordinal)),
            cookie =>
            {
                Assert.DoesNotContain("provider-subject", cookie, StringComparison.Ordinal);
                Assert.DoesNotContain("access", cookie, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Private_WithoutASession_IsNotServed()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());

        using HttpResponseMessage response = await browser.GetOnceAsync("/private");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Private_WithoutASession_IsSentToSignIn()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());

        using HttpResponseMessage response = await browser.GetOnceAsync("/private");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/signin?returnTo=%2Fprivate", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Private_WithAQueryString_KeepsItOnTheWayToSignIn()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());

        using HttpResponseMessage response = await browser.GetOnceAsync("/private?page=2");

        Assert.Equal("/signin?returnTo=%2Fprivate%3Fpage%3D2", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Private_WithoutASession_SignsInAndArrivesWhereItWasGoing()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());

        using HttpResponseMessage response = await browser.GetAsync("/private");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("private:", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Public_WithoutASession_IsServed()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());

        using HttpResponseMessage response = await browser.GetOnceAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Callback_ReplayedWithTheSameState_IsRefused()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());

        using HttpResponseMessage redirect = await browser.GetOnceAsync("/signin");
        string authorize = redirect.Headers.Location!.ToString();
        using HttpResponseMessage back = await browser.GetOnceAsync(authorize);
        string callback = back.Headers.Location!.ToString();

        using HttpResponseMessage first = await browser.GetOnceAsync(callback);
        using HttpResponseMessage second = await browser.GetOnceAsync(callback);

        // state_mismatch, not state_used: the first callback deleted this browser's correlator, so
        // the replay is refused before the flow store is consulted at all. That the STORE also
        // refuses a second take is pinned in the unit tier, where the cookie can be supplied twice.
        Assert.DoesNotContain("error=", first.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "error=state_mismatch", second.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, host.Provider.TokenRequests);
    }

    [Fact]
    public async Task Callback_WithAStateNothingStarted_MintsNoSession()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());

        using HttpResponseMessage response = await browser.GetOnceAsync(
            "/signin/callback?code=made-up&state=made-up");

        Assert.Contains("error=", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.False(browser.Holds("__Host-signin"));
    }

    [Fact]
    public async Task Callback_WhenTheProviderRefuses_ReportsAFailureAndMintsNoSession()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        host.Provider.RefuseWith = "access_denied";
        using var browser = new Browser(host.Client());

        using HttpResponseMessage redirect = await browser.GetOnceAsync("/signin");
        using HttpResponseMessage back = await browser.GetOnceAsync(redirect.Headers.Location!.ToString());
        using HttpResponseMessage response = await browser.GetOnceAsync(back.Headers.Location!.ToString());

        Assert.Contains(
            "error=signin_failed", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.False(browser.Holds("__Host-signin"));
    }

    [Fact]
    public async Task Callback_WhenTheProviderIsDown_DoesNotSignAnybodyIn()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        host.Provider.TokenEndpointIsDown = true;
        using var browser = new Browser(host.Client());

        using HttpResponseMessage redirect = await browser.GetOnceAsync("/signin");
        using HttpResponseMessage back = await browser.GetOnceAsync(redirect.Headers.Location!.ToString());
        using HttpResponseMessage response = await browser.GetOnceAsync(back.Headers.Location!.ToString());

        Assert.Contains("error=", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.False(browser.Holds("__Host-signin"));
    }

    [Fact]
    public async Task SignIn_RefusedByThePolicy_NeverReachesTheProvider()
    {
        await using SignInHost host = await SignInHost.StartAsync(
            builder => builder.WithSignInPolicy((_, _) => ValueTask.FromResult(false)));
        using var browser = new Browser(host.Client());

        using HttpResponseMessage response = await browser.GetOnceAsync("/signin");

        Assert.Contains("error=refused", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, host.Provider.TokenRequests);
    }

    [Fact]
    public async Task SignIn_WithAResolver_MintsAgainstTheLocalKey()
    {
        await using SignInHost host = await SignInHost.StartAsync(
            builder => builder.OnSignedIn((_, subject, _, _) =>
                ValueTask.FromResult<string?>("local-" + subject.Value)));
        host.Provider.Subject = "provider-subject-7";
        using var browser = new Browser(host.Client());

        await browser.GetAsync("/signin");
        using HttpResponseMessage response = await browser.GetAsync("/private");

        Assert.Equal("private:local-provider-subject-7", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignIn_WhenTheResolverRefuses_MintsNoSession()
    {
        await using SignInHost host = await SignInHost.StartAsync(
            builder => builder.OnSignedIn((_, _, _, _) => ValueTask.FromResult<string?>(null)));
        using var browser = new Browser(host.Client());

        using HttpResponseMessage redirect = await browser.GetOnceAsync("/signin");
        using HttpResponseMessage back = await browser.GetOnceAsync(redirect.Headers.Location!.ToString());
        using HttpResponseMessage response = await browser.GetOnceAsync(back.Headers.Location!.ToString());

        Assert.Contains("error=refused", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.False(browser.Holds("__Host-signin"));
    }

    [Fact]
    public async Task SignOut_LeavesTheNextRequestAnonymous()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());
        await browser.GetAsync("/signin");

        using HttpResponseMessage tokenResponse = await browser.GetOnceAsync("/antiforgery");
        string token = await tokenResponse.Content.ReadAsStringAsync();

        using HttpResponseMessage signedOut = await browser.PostAsync(
            "/signout", [new KeyValuePair<string, string>("__RequestVerificationToken", token)]);

        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.False(browser.Holds("__Host-signin"));

        using HttpResponseMessage after = await browser.GetOnceAsync("/private");
        Assert.NotEqual(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task SignOut_WithoutAnAntiforgeryToken_KeepsTheSession()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());
        await browser.GetAsync("/signin");

        using HttpResponseMessage refused = await browser.PostAsync("/signout", []);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // GetOnceAsync, not GetAsync. GetAsync follows redirects, and the demo provider approves
        // without asking, so a session that HAD been ended would redirect to /signin, walk the
        // whole flow again and arrive at /private with a 200 — the same assertion, passing for the
        // opposite reason. One hop is what distinguishes "still signed in" from "signed in again".
        using HttpResponseMessage after = await browser.GetOnceAsync("/private");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task MintedSession_FromTheTestingPackage_IsAcceptedByTheHost()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using HttpClient client = host.Client();

        string cookieValue = await TestSession.MintAsync(host.Services, "minted-subject");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/private");
        request.Headers.Add("Cookie", TestSession.CookieHeader(host.Services, cookieValue));
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("private:minted-subject", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExpiredSession_FromTheTestingPackage_IsRefusedByTheHost()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using HttpClient client = host.Client();

        string cookieValue = await TestSession.MintExpiredAsync(host.Services, "minted-subject");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/private");
        request.Headers.Add("Cookie", TestSession.CookieHeader(host.Services, cookieValue));
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TamperedSession_FromTheTestingPackage_IsRefusedByTheHost()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using HttpClient client = host.Client();

        string cookieValue = await TestSession.MintAsync(host.Services, "minted-subject");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/private");
        request.Headers.Add(
            "Cookie", TestSession.CookieHeader(host.Services, TestSession.Tamper(cookieValue)));
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_WithAnActiveBearerToken_IsServed()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using HttpClient client = host.Client();
        string token = host.Provider.IssueAccessToken("api-subject");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/whoami");
        request.Headers.Add("Authorization", "Bearer " + token);
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_WithARevokedBearerToken_IsRefused()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using HttpClient client = host.Client();
        string token = host.Provider.IssueAccessToken("api-subject");
        host.Provider.Revoked.Add(token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/whoami");
        request.Headers.Add("Authorization", "Bearer " + token);
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_WithNoBearerToken_IsRefused()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using HttpClient client = host.Client();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/whoami", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenHolder_AfterSignIn_HandsBackATokenTheProviderAccepts()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        host.Provider.Subject = "token-holder-subject";
        using var browser = new Browser(host.Client());

        await browser.GetAsync("/signin");
        using HttpResponseMessage held = await browser.GetOnceAsync("/token-holder");
        string token = await held.Content.ReadAsStringAsync();

        Assert.NotEqual("none", token);

        // The proof is the provider's, not ours: present what came back where the provider expects
        // its own access token, and see the right subject come out.
        using HttpClient direct = host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/stub/userinfo");
        request.Headers.Add("Authorization", "Bearer " + token);
        using HttpResponseMessage me = await direct.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Contains(
            "token-holder-subject", await me.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenHolder_WithoutASession_HasNoToken()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());

        using HttpResponseMessage held = await browser.GetOnceAsync("/token-holder");

        Assert.Equal("none", await held.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TokenHolder_AfterSignOut_HasNoToken()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using var browser = new Browser(host.Client());

        await browser.GetAsync("/signin");
        using HttpResponseMessage token = await browser.GetOnceAsync("/antiforgery");
        await browser.PostAsync(
            "/signout",
            [new KeyValuePair<string, string>(
                "__RequestVerificationToken", await token.Content.ReadAsStringAsync())]);

        using HttpResponseMessage held = await browser.GetOnceAsync("/token-holder");

        Assert.Equal("none", await held.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Host_ResolvesEverySchemeServiceAtStartup()
    {
        await using SignInHost host = await SignInHost.StartAsync();
        using IServiceScope scope = host.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<SessionManager>(
            SessionAuthenticationHandler.SchemeName));
    }
}
