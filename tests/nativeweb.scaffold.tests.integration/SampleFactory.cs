using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NativeWeb.Scaffold.Tests.Integration;

/// <summary>
/// A fresh <c>apps/sample</c> host per test.
/// </summary>
/// <remarks>
/// Per test rather than per class on purpose: the demo store is an in-memory
/// singleton seeded at startup, so a create or a delete in one test would leak into
/// the next. Standing the host up costs a few milliseconds and buys back the
/// ability to assert on exact row counts.
/// </remarks>
internal sealed class SampleFactory : WebApplicationFactory<NativeWeb.Sample.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // /signin is Development-only, and it is the only way in.
        builder.UseEnvironment("Development");

        // Force the in-memory store, rather than relying on the key being absent.
        // Configuration reads environment variables, and CI exports
        // ConnectionStrings__Sample for the JauntyQ tiers in the same job -- without
        // this, every test in this class would silently move onto PostgreSQL, where
        // the seeded rows they assert on do not exist. UseSetting outranks the
        // environment variable under CreateSlimBuilder, which is the same lever
        // SampleJauntyFactory uses in the other direction.
        builder.UseSetting("ConnectionStrings:Sample", string.Empty);
    }

    internal HttpClient CreateFollowingNothing() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>A client already carrying the admin session cookie.</summary>
    internal async Task<HttpClient> CreateSignedInAsync()
    {
        HttpClient client = CreateFollowingNothing();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/signin", UriKind.Relative));

        if (response.StatusCode != HttpStatusCode.Found)
        {
            throw new InvalidOperationException($"Demo sign-in did not redirect; got {(int)response.StatusCode}.");
        }

        return client;
    }
}

internal static class Forms
{
    private static readonly Regex TokenPattern = new(
        """<input type="hidden" name="([^"]+)" value="([^"]*)">""",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Pulls the antiforgery field out of a rendered page, so a test posts the way a
    /// browser would rather than by bypassing the guard it is meant to respect.
    /// </summary>
    internal static async Task<KeyValuePair<string, string>> AntiforgeryFieldAsync(HttpClient client, string path)
    {
        using HttpResponseMessage page = await client.GetAsync(new Uri(path, UriKind.Relative));
        page.EnsureSuccessStatusCode();

        string html = await page.Content.ReadAsStringAsync();

        // By name, not by position. This took the first hidden input on the page until
        // spec 017, when the list's search form gained hidden sort and dir fields that
        // render ahead of the token -- so every post from the list page carried "sort"
        // as its antiforgery token and came back 400. The framework's own field is the
        // "__"-prefixed one; every other hidden input on these pages is app-declared.
        foreach (Match match in TokenPattern.Matches(html))
        {
            if (match.Groups[1].Value.StartsWith("__", StringComparison.Ordinal))
            {
                return new KeyValuePair<string, string>(match.Groups[1].Value, match.Groups[2].Value);
            }
        }

        throw new InvalidOperationException($"No antiforgery field on {path}.");
    }

    internal static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string tokenPath,
        string action,
        params KeyValuePair<string, string>[] fields)
    {
        KeyValuePair<string, string> token = await AntiforgeryFieldAsync(client, tokenPath);

        using var content = new FormUrlEncodedContent([token, .. fields]);
        return await client.PostAsync(new Uri(action, UriKind.Relative), content);
    }
}
