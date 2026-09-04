using System.Net;

using NativeWeb.Security;

using Xunit;

namespace NativeWeb.Sample.Tests.Integration;

/// <summary>
/// The three public forms of spec 027, through the sample's real pipeline.
/// </summary>
public sealed class PublicFormsTests
{
    [Fact]
    public async Task Every_public_form_is_reachable_from_the_navigation()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        string home = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("href=\"/contact\"", home, StringComparison.Ordinal);
        Assert.Contains("href=\"/upload\"", home, StringComparison.Ordinal);
        Assert.Contains("href=\"/survey\"", home, StringComparison.Ordinal);
    }

    /// <summary>
    /// The trap markup lives in a template, and the names it uses are constants in
    /// <see cref="BotTraps"/>. Nothing but this compares the two, so a renamed
    /// constant would leave a form rendering fields the inspector no longer looks at
    /// and every submission tripping.
    /// </summary>
    [Theory]
    [InlineData("/contact")]
    [InlineData("/upload")]
    [InlineData("/survey")]
    public async Task Every_public_form_renders_the_trap_fields_the_inspector_reads(string path)
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        Dictionary<string, string> carried = await Pages.CarriedFieldsAsync(client, path);

        Assert.Equal(BotTraps.HiddenFilledValue, carried[BotTraps.HiddenFilledName]);
        Assert.Equal("", carried[BotTraps.HiddenEmptyName]);
        Assert.Equal(BotTraps.OffscreenFilledValue, carried[BotTraps.OffscreenFilledName]);
        Assert.Equal("", carried[BotTraps.OffscreenEmptyName]);
        Assert.NotEqual("", carried[BotTraps.CanaryName]);
    }

    [Fact]
    public async Task A_completed_contact_form_reaches_the_transport()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        Dictionary<string, string> carried = await Pages.CarriedFieldsAsync(client, "/contact");

        using HttpResponseMessage response = await Pages.PostAsync(
            client,
            carried,
            "/contact",
            ("name", "Ada"),
            ("email", "ada@example.test"),
            ("message", "Hello from the reference application."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "was sent to ada@example.test",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        (string to, NativeWeb.Mail.EmailContent content) = Assert.Single(factory.Mail.Sent);
        Assert.Equal("ada@example.test", to);
        Assert.Contains("Ada", content.Subject, StringComparison.Ordinal);
        Assert.Contains("Hello from the reference application.", content.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// R9. A mail server that is down is not this application failing, and a visitor
    /// gets told so rather than getting an error page.
    /// </summary>
    [Fact]
    public async Task A_refusing_transport_is_answered_with_a_page_and_not_a_500()
    {
        using var factory = new SampleFormsFactory();
        factory.Mail.Refuses = true;
        using HttpClient client = factory.CreateBrowser();

        Dictionary<string, string> carried = await Pages.CarriedFieldsAsync(client, "/contact");

        using HttpResponseMessage response = await Pages.PostAsync(
            client,
            carried,
            "/contact",
            ("name", "Ada"),
            ("email", "ada@example.test"),
            ("message", "Hello."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "could not be sent",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Spec 019 R23: a tripped form takes the normal-outcome path with no side
    /// effect. The page says what a delivered message says, and nothing was sent --
    /// which is the only way to tell the two apart from outside.
    /// </summary>
    [Fact]
    public async Task A_tripped_trap_sends_nothing_and_says_nothing_about_it()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        Dictionary<string, string> carried = await Pages.CarriedFieldsAsync(client, "/contact");
        carried[BotTraps.HiddenEmptyName] = "a bot filled this in";

        using HttpResponseMessage response = await Pages.PostAsync(
            client,
            carried,
            "/contact",
            ("name", "Ada"),
            ("email", "ada@example.test"),
            ("message", "Hello."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("was sent", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(factory.Mail.Sent);
    }

    /// <summary>
    /// R10. The token is attached by <c>MapFormPost</c> itself, so this is asserting
    /// that the mapping call did it rather than that the handler remembered to.
    /// </summary>
    [Fact]
    public async Task A_post_carrying_no_antiforgery_token_is_refused_and_sends_nothing()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        Dictionary<string, string> carried = await Pages.CarriedFieldsAsync(client, "/contact");
        carried.Remove(Pages.AntiforgeryFieldName(carried));

        using HttpResponseMessage response = await Pages.PostAsync(
            client,
            carried,
            "/contact",
            ("name", "Ada"),
            ("email", "ada@example.test"),
            ("message", "Hello."));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Mail.Sent);
    }

    [Fact]
    public async Task An_incomplete_contact_form_is_reported_and_sends_nothing()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        Dictionary<string, string> carried = await Pages.CarriedFieldsAsync(client, "/contact");

        using HttpResponseMessage response = await Pages.PostAsync(
            client, carried, "/contact", ("name", "Ada"), ("email", "not-an-address"), ("message", "Hello."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(factory.Mail.Sent);
    }

    [Fact]
    public async Task An_uploaded_picture_is_stored_and_served_back()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        Dictionary<string, string> carried = await Pages.CarriedFieldsAsync(client, "/upload");

        using HttpResponseMessage response = await Pages.PostPictureAsync(
            client, carried, "holiday.png", "image/png", Pages.Png(), caption: "A caption");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string stored = StoredId(await response.Content.ReadAsStringAsync());

        using HttpResponseMessage served = await client.GetAsync(new Uri("/uploads/" + stored, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Pages.Png(), await served.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// R13. The name the client sent is metadata; the stored name came from the
    /// store. A name built to escape the directory is stored under a name that has
    /// nothing to do with it, and nothing lands outside the directory.
    /// </summary>
    [Fact]
    public async Task A_traversing_file_name_never_becomes_a_path()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        Dictionary<string, string> carried = await Pages.CarriedFieldsAsync(client, "/upload");

        using HttpResponseMessage response = await Pages.PostPictureAsync(
            client, carried, "../../etc/passwd.png", "image/png", Pages.Png());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string stored = StoredId(await response.Content.ReadAsStringAsync());

        Assert.DoesNotContain("..", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("passwd", stored, StringComparison.Ordinal);
        Assert.Equal(
            [Path.Combine(factory.UploadDirectory, stored)],
            Directory.GetFiles(factory.UploadDirectory));
        Assert.False(
            File.Exists(Path.Combine(factory.UploadDirectory, "..", "..", "etc", "passwd.png")),
            "a file was written outside the upload directory");
    }

    [Theory]
    [InlineData("/uploads/..%2F..%2Fappsettings.json")]
    [InlineData("/uploads/not-a-stored-name.png")]
    [InlineData("/uploads/0123456789abcdef0123456789abcdef.exe")]
    [InlineData("/uploads/0123456789abcdef0123456789abcdef.png")]
    public async Task A_request_for_anything_the_store_did_not_write_is_a_404(string path)
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_picture_outside_the_accepted_types_is_refused_and_stores_nothing()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        Dictionary<string, string> carried = await Pages.CarriedFieldsAsync(client, "/upload");

        using HttpResponseMessage response = await Pages.PostPictureAsync(
            client, carried, "notes.txt", "text/plain", Pages.Png());

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(Directory.GetFiles(factory.UploadDirectory));
    }

    [Fact]
    public async Task Both_survey_steps_arrive_together()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        string second = await StepTwoAsync(client, "a compiler");

        Assert.Contains("a compiler", second, StringComparison.Ordinal);

        using HttpResponseMessage done = await Pages.PostAsync(
            client, Pages.Carried(second), "/survey", ("comment", "It went well."));

        string html = await done.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.Contains("a compiler", html, StringComparison.Ordinal);
        Assert.Contains("It went well.", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// R15. Submitting step two twice records two responses, and the page says so
    /// rather than pretending the second submit changed nothing.
    /// </summary>
    [Fact]
    public async Task Submitting_step_two_twice_records_two_responses()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        string second = await StepTwoAsync(client, "a compiler");
        Dictionary<string, string> carried = Pages.Carried(second);

        using (HttpResponseMessage first = await Pages.PostAsync(
            client, carried, "/survey", ("comment", "It went well.")))
        {
            Assert.Contains("started: 1", await first.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using HttpResponseMessage repeat = await Pages.PostAsync(
            client, carried, "/survey", ("comment", "It went well."));

        Assert.Contains("started: 2", await repeat.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tampered_carried_answer_says_to_start_again_and_does_not_read()
    {
        using var factory = new SampleFormsFactory();
        using HttpClient client = factory.CreateBrowser();

        string second = await StepTwoAsync(client, "a compiler");
        Dictionary<string, string> carried = Pages.Carried(second);
        carried["carried"] = Flip(carried["carried"]);

        using HttpResponseMessage response = await Pages.PostAsync(
            client, carried, "/survey", ("comment", "It went well."));

        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("could not be read back", html, StringComparison.Ordinal);
        Assert.DoesNotContain("It went well.", html, StringComparison.Ordinal);
    }

    private static async Task<string> StepTwoAsync(HttpClient client, string role)
    {
        Dictionary<string, string> carried = await Pages.CarriedFieldsAsync(client, "/survey");

        using HttpResponseMessage response = await Pages.PostAsync(
            client, carried, "/survey/step-2", ("role", role));

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static string StoredId(string html)
    {
        int at = html.IndexOf("/uploads/", StringComparison.Ordinal);
        Assert.True(at >= 0, "the page named no stored upload");

        int end = html.IndexOf('"', at);
        return html[(at + "/uploads/".Length)..end];
    }

    // Base64url alphabet, so the flipped character keeps the value decodable and the
    // refusal comes from authentication rather than from a malformed payload.
    private static string Flip(string value)
    {
        char[] characters = value.ToCharArray();
        int at = characters.Length / 2;
        characters[at] = characters[at] == 'A' ? 'B' : 'A';
        return new string(characters);
    }
}
