using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using NativeWeb.Sample;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// <c>apps/sample</c> as it runs anywhere but Development: the passphrase form is the
/// way in (spec 020).
/// </summary>
/// <remarks>
/// https, because outside Development the session and antiforgery cookies are
/// <c>Secure</c> and <see cref="CookieContainer"/> will not return one over http.
/// </remarks>
internal sealed class PassphraseSampleFactory(string? passphrase) : WebApplicationFactory<NativeWeb.Sample.Program>
{
    internal const string Secret = "correct horse battery staple";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Staging");
        builder.UseSetting("ConnectionStrings:Sample", string.Empty);
        builder.UseSetting($"Sample:{AdminPassphrase.Key}", passphrase ?? string.Empty);
    }

    internal HttpClient CreateFollowingNothing() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
}

public sealed class AdminSignInTests
{
    [Fact]
    public async Task TheFormIsServedWithAPassphraseFieldAndAToken()
    {
        using var factory = new PassphraseSampleFactory(PassphraseSampleFactory.Secret);
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/signin", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"passphrase\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"password\"", html, StringComparison.Ordinal);
        Assert.Matches("name=\"__[^\"]+\" value=\"[^\"]+\"", html);
        Assert.DoesNotContain("Not accepted", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRightPassphraseSignsInAndTheAdminThenListsEveryModel()
    {
        using var factory = new PassphraseSampleFactory(PassphraseSampleFactory.Secret);
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage before = await client.GetAsync(new Uri("/admin", UriKind.Relative));
        string anonymous = await before.Content.ReadAsStringAsync();

        using HttpResponseMessage signIn = await PostAsync(client, PassphraseSampleFactory.Secret);

        using HttpResponseMessage after = await client.GetAsync(new Uri("/admin", UriKind.Relative));
        string signedIn = await after.Content.ReadAsStringAsync();

        Assert.DoesNotContain("href=\"/admin/articles\"", anonymous, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Found, signIn.StatusCode);
        Assert.Equal("/admin", signIn.Headers.Location?.ToString());
        Assert.Contains("href=\"/admin/articles\"", signedIn, StringComparison.Ordinal);
        Assert.Contains("href=\"/admin/tags\"", signedIn, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/admin/articles", "/admin/articles")]
    [InlineData("//evil.example/", "/admin")]
    [InlineData("https://evil.example/", "/admin")]
    public async Task AfterSigningInTheVisitorReturnsToALocalPathOnly(string returnUrl, string expected)
    {
        using var factory = new PassphraseSampleFactory(PassphraseSampleFactory.Secret);
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage signIn = await PostAsync(client, PassphraseSampleFactory.Secret, returnUrl);

        Assert.Equal(HttpStatusCode.Found, signIn.StatusCode);
        Assert.Equal(expected, signIn.Headers.Location?.ToString());
    }

    [Fact]
    public async Task EveryRefusalIsTheSame401Page()
    {
        using var factory = new PassphraseSampleFactory(PassphraseSampleFactory.Secret);
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage empty = await PostAsync(client, "");
        using HttpResponseMessage sameLength = await PostAsync(client, "correct horse battery stapel");
        using HttpResponseMessage otherLength = await PostAsync(client, "x");

        string emptyBody = WithoutToken(await empty.Content.ReadAsStringAsync());
        string sameLengthBody = WithoutToken(await sameLength.Content.ReadAsStringAsync());
        string otherLengthBody = WithoutToken(await otherLength.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, empty.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sameLength.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherLength.StatusCode);
        Assert.Contains("Not accepted", emptyBody, StringComparison.Ordinal);
        Assert.Equal(emptyBody, sameLengthBody);
        Assert.Equal(emptyBody, otherLengthBody);
        Assert.DoesNotContain(
            empty.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies) ? cookies : [],
            c => c.StartsWith("nw-sample=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThePostIsRateLimitedBelowTheGeneralPolicy()
    {
        using var factory = new PassphraseSampleFactory(PassphraseSampleFactory.Secret);
        using HttpClient client = factory.CreateFollowingNothing();

        var statuses = new List<HttpStatusCode>();
        for (int i = 0; i < AdminSignIn.PermitLimit + 1; i++)
        {
            using HttpResponseMessage response = await PostAsync(client, "wrong");
            statuses.Add(response.StatusCode);
        }

        using HttpResponseMessage right = await PostAsync(client, PassphraseSampleFactory.Secret);

        Assert.Equal(AdminSignIn.PermitLimit, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.Equal(HttpStatusCode.TooManyRequests, right.StatusCode);
    }

    [Fact]
    public async Task WithNoSecretConfiguredOutsideProductionTheFormRendersAndRefusesEverything()
    {
        using var factory = new PassphraseSampleFactory(null);
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage form = await client.GetAsync(new Uri("/signin", UriKind.Relative));
        using HttpResponseMessage empty = await PostAsync(client, "");
        using HttpResponseMessage guess = await PostAsync(client, PassphraseSampleFactory.Secret);

        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, empty.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, guess.StatusCode);
    }

    [Fact]
    public async Task SigningOutClearsTheSessionAndTheAdminRedirectsAgain()
    {
        using var factory = new PassphraseSampleFactory(PassphraseSampleFactory.Secret);
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage signIn = await PostAsync(client, PassphraseSampleFactory.Secret);
        using HttpResponseMessage whileSignedIn = await client.GetAsync(new Uri("/admin/articles", UriKind.Relative));
        using HttpResponseMessage signInPage = await client.GetAsync(new Uri("/signin", UriKind.Relative));
        string signedInPage = await signInPage.Content.ReadAsStringAsync();

        KeyValuePair<string, string> token = await Forms.AntiforgeryFieldAsync(client, "/signin");
        using HttpResponseMessage signOut = await client.PostAsync(
            new Uri("/signout", UriKind.Relative),
            new FormUrlEncodedContent([token]));
        using HttpResponseMessage afterwards = await client.GetAsync(new Uri("/admin/articles", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, signIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, whileSignedIn.StatusCode);
        Assert.Contains("Signed in as operator", signedInPage, StringComparison.Ordinal);
        Assert.Contains("action=\"/signout\"", signedInPage, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Found, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.Found, afterwards.StatusCode);
        Assert.StartsWith("https://localhost/signin", afterwards.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SigningOutWithoutATokenIsRefused()
    {
        using var factory = new PassphraseSampleFactory(PassphraseSampleFactory.Secret);
        using HttpClient client = factory.CreateFollowingNothing();

        using HttpResponseMessage signIn = await PostAsync(client, PassphraseSampleFactory.Secret);
        using HttpResponseMessage signOut = await client.PostAsync(
            new Uri("/signout", UriKind.Relative),
            new FormUrlEncodedContent([]));
        using HttpResponseMessage afterwards = await client.GetAsync(new Uri("/admin/articles", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, signIn.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterwards.StatusCode);
    }

    [Fact]
    public async Task ProductionWithoutTheSecretExitsNamingTheKey()
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "nativeweb.sample.dll");
        Assert.True(File.Exists(dll), $"{dll} is not beside the tests; the sample project reference should copy it.");

        var startInfo = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        startInfo.EnvironmentVariables.Remove("ConnectionStrings__Sample");
        startInfo.EnvironmentVariables.Remove("Sample__Admin__Passphrase");

        using Process process = Process.Start(startInfo)!;
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        bool exited = process.WaitForExit(60_000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
        }

        Assert.True(exited, "the sample did not exit within a minute");
        Assert.Equal(78, process.ExitCode);
        Assert.Contains("Sample:Admin:Passphrase", await stderr, StringComparison.Ordinal);
        Assert.Contains("secrets.env", await stderr, StringComparison.Ordinal);
        _ = await stdout;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string passphrase, string? returnUrl = null)
    {
        KeyValuePair<string, string> token = await Forms.AntiforgeryFieldAsync(client, "/signin");
        var fields = new List<KeyValuePair<string, string>>
        {
            token,
            new("passphrase", passphrase),
        };

        if (returnUrl is not null)
        {
            fields.Add(new("return", returnUrl));
        }

        return await client.PostAsync(new Uri("/signin", UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private static string WithoutToken(string html) =>
        Regex.Replace(
            html,
            "(name=\"__[^\"]+\" value=|name=\"request-verification-token\" content=)\"[^\"]*\"",
            "$1\"\"",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
}
