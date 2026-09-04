using Microsoft.Playwright;

using NativeWeb.Scaffold.Tests.E2e;

using Xunit;

namespace NativeWeb.Forms.Tests.E2e;

/// <summary>
/// The published sample on its own port, with no database and no SMTP host.
/// </summary>
/// <remarks>
/// <para>
/// The bot traps refuse a submission that arrives faster than a person could have
/// typed it. A browser driven by Playwright fills three fields in well under the
/// default three seconds, so every form here would take the tripped path and the
/// suite would pass while asserting nothing about the forms. The minimum age is
/// turned off rather than waited out.
/// </para>
/// <para>
/// No SMTP host is configured, which is not a gap in the fixture: it is the
/// condition R9 is about. The published binary puts <c>UnconfiguredEmailTransport</c>
/// in the transport slot, the contact form catches what it throws, and the visitor
/// gets a page. A configured host would prove the happy path and lose the one this
/// tier can reach without a mail server.
/// </para>
/// </remarks>
public sealed class FormSampleFixture() : SampleProcessFixture(
    5353,
    null,
    extraEnvironment: new Dictionary<string, string> { ["Sample__BotTraps__MinAgeSeconds"] = "0" });

/// <summary>One browser for this tier.</summary>
public sealed class FormBrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    /// <summary>The launched browser.</summary>
    public IBrowser Browser { get; private set; } = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        Browser = await _playwright.Chromium
            .LaunchAsync(new BrowserTypeLaunchOptions { Headless = true })
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await Browser.CloseAsync().ConfigureAwait(false);
        _playwright?.Dispose();
    }
}

/// <summary>
/// Serial, because every test shares one process.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FormE2eFixture
    : ICollectionFixture<FormSampleFixture>, ICollectionFixture<FormBrowserFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "FormE2e";
}

/// <summary>
/// Spec 027 R17: a browser completes each of the three public forms against the
/// published NativeAOT binary.
/// </summary>
/// <remarks>
/// What this tier reaches that the ones below it do not is the whole submit path a
/// person takes: the antiforgery token and the bot-trap fields as the templates
/// rendered them, the multipart body the browser built from a file input, and the
/// carried hidden field surviving a round trip through markup. The integration tier
/// assembles all of that by hand, so it proves the handlers and not the pages.
/// </remarks>
[Collection(FormE2eFixture.Name)]
public sealed class PublicFormFlowTests(FormSampleFixture sample, FormBrowserFixture browser)
{
    // The smallest thing Chromium will hand over as image/png. Nothing decodes it;
    // the accepted-type check reads the part's declared content type.
    private static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    [Fact]
    public async Task ContactForm_SubmittedWithNoMailServer_AnswersAPageAndNotAnError()
    {
        await using IBrowserContext context = await browser.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();

        IResponse? response = await page.GotoAsync($"{sample.BaseUrl}/contact");
        Assert.Equal(200, response?.Status);

        await page.FillAsync("#name", "Ada");
        await page.FillAsync("#email", "ada@example.test");
        await page.FillAsync("#message", "Hello from a browser.");
        await page.ClickAsync("button[type=submit]");

        Assert.Contains(
            "could not be sent",
            await page.InnerTextAsync("body"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("Something went wrong", await page.InnerTextAsync("body"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadForm_SubmittedWithAPicture_StoresItAndServesItBack()
    {
        await using IBrowserContext context = await browser.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync($"{sample.BaseUrl}/upload");

        await page.FillAsync("#caption", "A caption");
        await page.SetInputFilesAsync(
            "#picture",
            new FilePayload { Name = "holiday.png", MimeType = "image/png", Buffer = Picture });
        await page.ClickAsync("button[type=submit]");

        // The link's text is the stored name, which is what proves the client's name
        // did not become it: "holiday.png" was sent and something else came back.
        string stored = await page.InnerTextAsync(".notice a");

        Assert.NotEqual("holiday.png", stored);
        Assert.EndsWith(".png", stored, StringComparison.Ordinal);
        Assert.Contains("holiday.png", await page.InnerTextAsync(".upload__original"), StringComparison.Ordinal);

        IResponse? served = await page.GotoAsync($"{sample.BaseUrl}/uploads/{stored}");

        Assert.Equal(200, served?.Status);
        Assert.Equal("image/png", served?.Headers["content-type"]);
        Assert.Equal(Picture, await served!.BodyAsync());
    }

    /// <summary>
    /// The declared ceiling is 1 MB, and this is well under it. It is over the
    /// framework's own default 128 KiB request-body limit, which is what this asserts
    /// does not apply to a route that declared its own.
    /// </summary>
    [Fact]
    public async Task UploadForm_GivenAPictureUnderTheDeclaredCeiling_AcceptsIt()
    {
        await using IBrowserContext context = await browser.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync($"{sample.BaseUrl}/upload");

        await page.SetInputFilesAsync(
            "#picture",
            new FilePayload
            {
                Name = "large.png",
                MimeType = "image/png",
                Buffer = new byte[300 * 1024],
            });
        await page.ClickAsync("button[type=submit]");

        Assert.Equal(1, await page.Locator(".notice a").CountAsync());
    }

    [Fact]
    public async Task UploadForm_GivenAFileOutsideTheAcceptedSet_RefusesIt()
    {
        await using IBrowserContext context = await browser.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync($"{sample.BaseUrl}/upload");

        await page.SetInputFilesAsync(
            "#picture",
            new FilePayload { Name = "notes.txt", MimeType = "text/plain", Buffer = Picture });

        IResponse? refused = await page.RunAndWaitForResponseAsync(
            () => page.ClickAsync("button[type=submit]"),
            r => r.Url.EndsWith("/upload", StringComparison.Ordinal) && r.Request.Method == "POST");

        Assert.Equal(415, refused?.Status);
    }

    [Fact]
    public async Task Survey_CompletedInTwoSteps_CarriesTheFirstAnswerIntoTheSecond()
    {
        await using IBrowserContext context = await browser.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync($"{sample.BaseUrl}/survey");

        await page.FillAsync("#role", "a compiler");
        await page.ClickAsync("button[type=submit]");

        Assert.Contains("a compiler", await page.InnerTextAsync(".survey__answered"), StringComparison.Ordinal);

        await page.FillAsync("#comment", "It went well.");
        await page.ClickAsync("button[type=submit]");

        string done = await page.InnerTextAsync("body");

        // Both answers on the completion page is the whole of what the carried field
        // buys: the second request carried an answer the server never stored.
        Assert.Contains("a compiler", done, StringComparison.Ordinal);
        Assert.Contains("It went well.", done, StringComparison.Ordinal);
    }

    /// <summary>
    /// R14. The carried field is signed, and the browser is where a person could edit
    /// it. Edited, it is refused with an instruction rather than being read.
    /// </summary>
    [Fact]
    public async Task Survey_WithTheCarriedFieldEditedInThePage_SaysToStartAgain()
    {
        await using IBrowserContext context = await browser.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync($"{sample.BaseUrl}/survey");
        await page.FillAsync("#role", "a compiler");
        await page.ClickAsync("button[type=submit]");

        await page.EvalOnSelectorAsync(
            "input[name=carried]",
            "element => element.value = element.value.slice(0, -4) + 'AAAA'");

        await page.FillAsync("#comment", "It went well.");
        await page.ClickAsync("button[type=submit]");

        string body = await page.InnerTextAsync("body");

        Assert.Contains("could not be read back", body, StringComparison.Ordinal);
        Assert.DoesNotContain("It went well.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryPublicForm_IsReachableFromTheNavigation()
    {
        await using IBrowserContext context = await browser.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync(sample.BaseUrl);

        foreach (string path in new[] { "/contact", "/upload", "/survey" })
        {
            Assert.Equal(1, await page.Locator($"nav a[href='{path}']").CountAsync());
        }
    }
}
