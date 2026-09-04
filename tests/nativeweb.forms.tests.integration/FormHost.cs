using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NativeWeb.Forms;

namespace NativeWeb.Forms.Tests.Integration;

/// <summary>
/// A form model carrying one required file and one text field.
/// </summary>
[Form]
public sealed partial record DocumentForm(
    [FormField("caption")] string? Caption,
    [FormFile(MaxBytes = 4096, Accept = ["text/plain"])] FormFile Document);

/// <summary>
/// The same file, optional, and with no accepted-type set.
/// </summary>
[Form]
public sealed partial record OptionalDocumentForm(
    [FormFile(MaxBytes = 4096)] FormFile? Document);

/// <summary>
/// Two routes over a real request pipeline, spec 027 R1-R5.
/// </summary>
/// <remarks>
/// <para>
/// The unit tier drives the generator and asserts the emitted text; nothing there
/// proves the emitted text does anything to a request. This tier posts real
/// multipart bodies at a real pipeline and reads the status back.
/// </para>
/// <para>
/// <b>The antiforgery token rides in a header, not a hidden field.</b>
/// <c>MapFormPost</c> attaches <c>IAntiforgeryMetadata</c> by construction, so
/// <c>UseAntiforgery</c> is mandatory and every post here has to carry a token. A
/// hidden field would have to be spliced into a multipart body -- and into the JSON
/// body of the fact about a JSON body, where no such field can exist. The header
/// carries the same token through the same validation.
/// </para>
/// </remarks>
internal sealed class FormHost : IAsyncDisposable
{
    private const string TokenHeader = "RequestVerificationToken";

    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private FormHost(WebApplication app, HttpClient client)
    {
        _app = app;
        _client = client;
    }

    internal static async Task<FormHost> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAntiforgery(options => options.HeaderName = TokenHeader);

        WebApplication app = builder.Build();

        // The one clause of ErrorPageMiddleware (libs/nativeweb.web, line 37) this
        // tier needs. A real host turns the binder's BadHttpRequestException into a
        // response with the status it carries; without something in that role the
        // exception reaches the test client and every refusal here reads as a crash.
        // The status only -- not the message. What a refusal SAYS is asserted in the
        // unit tier against the binder, because a real host writes its own error page
        // and never echoes the exception.
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = ex.StatusCode;
            }
        });

        app.UseAntiforgery();

        app.MapGet("/token", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Text(antiforgery.GetAndStoreTokens(context).RequestToken ?? string.Empty));

        app.MapFormPost<DocumentForm>("/document", (form, _) => Task.FromResult(Describe(form.Caption, form.Document)));

        app.MapFormPost<OptionalDocumentForm>(
            "/optional",
            (form, _) => Task.FromResult(
                form.Document is null ? Results.Text("no file") : Describe(null, form.Document)));

        await app.StartAsync();

        return new FormHost(app, app.GetTestClient());
    }

    /// <summary>
    /// Posts <paramref name="content"/> with a freshly issued token and its cookie.
    /// </summary>
    internal async Task<HttpResponseMessage> PostAsync(string path, HttpContent content)
    {
        using HttpResponseMessage issued = await _client.GetAsync(new Uri("/token", UriKind.Relative));
        issued.EnsureSuccessStatusCode();

        string token = await issued.Content.ReadAsStringAsync(CancellationToken.None);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = content,
        };
        request.Headers.Add(TokenHeader, token);

        // The test client keeps no cookie jar, so the pair the validation needs is
        // reassembled by hand: the cookie half comes back on the /token response and
        // is echoed here.
        foreach (string cookie in issued.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
            ? values
            : [])
        {
            request.Headers.Add("Cookie", cookie.Split(';', 2)[0]);
        }

        return await _client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // The bound file is read here rather than counted, so a test asserting on the
    // content is asserting that the bytes survived the binder rather than that a
    // header did.
    private static IResult Describe(string? caption, FormFile file)
    {
        using var reader = new StreamReader(file.OpenReadStream());

        return Results.Text(
            string.Join('|', caption ?? "-", file.Name, file.ContentType, file.Length, reader.ReadToEnd()));
    }
}
