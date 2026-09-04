using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NativeWeb.Mail;
using NativeWeb.Security;

namespace NativeWeb.Sample.Tests.Integration;

/// <summary>
/// A fresh <c>apps/sample</c> host per test, with its uploads under a directory this
/// factory made and its mail transport replaced by one the test can read.
/// </summary>
/// <remarks>
/// The upload directory is made under this tier's own build output and removed on
/// dispose. It has to be per-factory: two hosts sharing one would let a leftover file
/// from a previous test satisfy an assertion about this one.
/// </remarks>
internal sealed class SampleFormsFactory : WebApplicationFactory<NativeWeb.Sample.Program>
{
    private readonly string _uploads = Path.Combine(
        AppContext.BaseDirectory, "uploads-" + Guid.NewGuid().ToString("n"));

    internal RecordingTransport Mail { get; } = new();

    internal string UploadDirectory => _uploads;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Force the in-memory store rather than relying on the key being absent: CI
        // exports ConnectionStrings__Sample for the JauntyQ tiers in the same job.
        builder.UseSetting("ConnectionStrings:Sample", string.Empty);
        builder.UseSetting("Sample:Uploads:Directory", _uploads);

        // The bot traps refuse a submission that arrives faster than a person could
        // have typed it, and every submission here is machine-fast by construction.
        builder.UseSetting("Sample:BotTraps:MinAgeSeconds", "0");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailTransport>();
            services.AddSingleton<IEmailTransport>(Mail);
        });
    }

    internal HttpClient CreateBrowser() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/"),
            HandleCookies = true,
            AllowAutoRedirect = true,
        });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_uploads))
        {
            Directory.Delete(_uploads, recursive: true);
        }
    }
}

/// <summary>
/// The substitute the sample's contact form sends through.
/// </summary>
/// <remarks>
/// It can be told to refuse, which is how the "SMTP is unreachable" path is reached
/// without an unreachable SMTP server. What the framework ships in that slot when
/// nothing is configured -- <c>UnconfiguredEmailTransport</c> -- refuses the same way.
/// </remarks>
internal sealed class RecordingTransport : IEmailTransport
{
    private readonly List<(string To, EmailContent Content)> _sent = [];

    internal bool Refuses { get; set; }

    internal IReadOnlyList<(string To, EmailContent Content)> Sent => _sent;

    public Task SendAsync(string toAddress, EmailContent content, CancellationToken cancellationToken)
    {
        if (Refuses)
        {
            throw new InvalidOperationException("this transport was told to refuse");
        }

        lock (_sent)
        {
            _sent.Add((toAddress, content));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Posting a page's form the way a browser would: with the token and the trap fields
/// that page rendered, rather than with a hand-written body that would bypass both.
/// </summary>
internal static class Pages
{
    // Deliberately not anchored on the closing bracket: two of the trap fields carry
    // an autocomplete attribute after the value, and a pattern that required ">" next
    // would silently drop them and every submission would look untrapped.
    private static readonly Regex FieldPattern = new(
        """<input type="(?:hidden|text)" name="([^"]+)" value="([^"]*)""",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    /// <summary>Every hidden and offscreen field a page rendered, by name.</summary>
    internal static async Task<Dictionary<string, string>> CarriedFieldsAsync(HttpClient client, string path)
    {
        using HttpResponseMessage page = await client.GetAsync(new Uri(path, UriKind.Relative));
        page.EnsureSuccessStatusCode();

        return Carried(await page.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The one hidden field on a page that is not a bot trap: the antiforgery token.
    /// </summary>
    internal static string AntiforgeryFieldName(Dictionary<string, string> carried)
    {
        ArgumentNullException.ThrowIfNull(carried);

        string[] traps =
        [
            BotTraps.HiddenFilledName,
            BotTraps.HiddenEmptyName,
            BotTraps.OffscreenFilledName,
            BotTraps.OffscreenEmptyName,
            BotTraps.CanaryName,
            "carried",
        ];

        return carried.Keys.Single(name => !traps.Contains(name, StringComparer.Ordinal));
    }

    internal static Dictionary<string, string> Carried(string html)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in FieldPattern.Matches(html))
        {
            fields[match.Groups[1].Value] = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value);
        }

        return fields;
    }

    // Both of these await inside the using rather than returning the task: TestServer
    // streams the request body, so a content disposed at the return statement is read
    // after it is closed and the post arrives empty with a 200 that means nothing.
    internal static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        Dictionary<string, string> carried,
        string action,
        params (string Name, string Value)[] fields)
    {
        var body = new List<KeyValuePair<string, string>>(
            carried.Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value)));

        body.AddRange(fields.Select(field => new KeyValuePair<string, string>(field.Name, field.Value)));

        using var content = new FormUrlEncodedContent(body);
        return await client.PostAsync(new Uri(action, UriKind.Relative), content);
    }

    internal static async Task<HttpResponseMessage> PostPictureAsync(
        HttpClient client,
        Dictionary<string, string> carried,
        string fileName,
        string contentType,
        byte[] bytes,
        string caption = "")
    {
        using var body = new MultipartFormDataContent();

        foreach (KeyValuePair<string, string> field in carried)
        {
            body.Add(new StringContent(field.Value), field.Key);
        }

        body.Add(new StringContent(caption), "caption");

        var picture = new ByteArrayContent(bytes);
        picture.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        body.Add(picture, "picture", fileName);

        return await client.PostAsync(new Uri("/upload", UriKind.Relative), body);
    }

    internal static byte[] Png() => Encoding.UTF8.GetBytes("not a real png, and nothing here decodes it");
}
