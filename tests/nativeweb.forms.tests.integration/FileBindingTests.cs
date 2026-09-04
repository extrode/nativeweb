using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Xunit;

namespace NativeWeb.Forms.Tests.Integration;

/// <summary>
/// Spec 027 R1-R5, through a real pipeline. See <see cref="FormHost"/> for what the
/// pipeline is and why antiforgery is not in it.
/// </summary>
public sealed class FileBindingTests
{
    [Fact]
    public async Task A_multipart_post_binds_the_file_and_the_text_field()
    {
        await using FormHost host = await FormHost.StartAsync();

        using MultipartFormDataContent body = Multipart(
            ("document", "notes.txt", "text/plain", "the contents"),
            caption: "a caption");

        using HttpResponseMessage response = await host.PostAsync("/document", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "a caption|notes.txt|text/plain|12|the contents",
            await response.Content.ReadAsStringAsync(CancellationToken.None));
    }

    /// <summary>
    /// R3. A urlencoded body cannot carry a file, so binding one would report the
    /// file missing and blame the client for a field they could not have sent.
    /// </summary>
    [Fact]
    public async Task A_urlencoded_post_to_a_file_model_is_refused_as_unsupported()
    {
        await using FormHost host = await FormHost.StartAsync();

        using var body = new FormUrlEncodedContent([new KeyValuePair<string, string>("caption", "a caption")]);

        using HttpResponseMessage response = await host.PostAsync("/document", body);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task A_json_body_is_still_refused_by_the_form_content_type_guard()
    {
        await using FormHost host = await FormHost.StartAsync();

        using var body = new StringContent("{}", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await host.PostAsync("/document", body);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    /// <summary>
    /// R4's size half, which is a real mid-read abort rather than a measurement
    /// taken after the whole body has been buffered.
    /// </summary>
    [Fact]
    public async Task A_body_past_the_declared_ceiling_is_refused_as_too_large()
    {
        await using FormHost host = await FormHost.StartAsync();

        using MultipartFormDataContent body = Multipart(
            ("document", "notes.txt", "text/plain", new string('x', 8000)));

        using HttpResponseMessage response = await host.PostAsync("/document", body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task A_body_inside_the_declared_ceiling_is_accepted()
    {
        await using FormHost host = await FormHost.StartAsync();

        using MultipartFormDataContent body = Multipart(
            ("document", "notes.txt", "text/plain", new string('x', 3000)));

        using HttpResponseMessage response = await host.PostAsync("/document", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// R4's content-type half. What the refusal says -- R5's "name the set" -- is
    /// asserted in the unit tier against the binder: a real host writes its own error
    /// page rather than echoing the exception, so a body assertion here would be
    /// about this tier's own middleware.
    /// </summary>
    [Fact]
    public async Task A_content_type_outside_the_accepted_set_is_refused()
    {
        await using FormHost host = await FormHost.StartAsync();

        using MultipartFormDataContent body = Multipart(
            ("document", "avatar.png", "image/png", "not really a png"));

        using HttpResponseMessage response = await host.PostAsync("/document", body);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    /// <summary>
    /// A browser sends the charset with the type. Comparing the whole header would
    /// refuse every text part any real client ever sends.
    /// </summary>
    [Fact]
    public async Task An_accepted_content_type_carrying_parameters_is_still_accepted()
    {
        await using FormHost host = await FormHost.StartAsync();

        using MultipartFormDataContent body = Multipart(
            ("document", "notes.txt", "text/plain; charset=utf-8", "the contents"));

        using HttpResponseMessage response = await host.PostAsync("/document", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_required_file_is_a_client_error()
    {
        await using FormHost host = await FormHost.StartAsync();

        using MultipartFormDataContent body = Multipart(caption: "a caption");

        using HttpResponseMessage response = await host.PostAsync("/document", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A part carrying the field's name and no bytes. Treating it as present would
    /// hand the handler an empty file where the visitor chose nothing.
    /// </summary>
    [Fact]
    public async Task An_empty_file_part_is_the_same_as_no_file_at_all()
    {
        await using FormHost host = await FormHost.StartAsync();

        using MultipartFormDataContent body = Multipart(("document", "empty.txt", "text/plain", ""));

        using HttpResponseMessage response = await host.PostAsync("/document", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_optional_file_left_out_binds_to_nothing_and_the_handler_runs()
    {
        await using FormHost host = await FormHost.StartAsync();

        using MultipartFormDataContent body = Multipart(caption: "a caption");

        using HttpResponseMessage response = await host.PostAsync("/optional", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no file", await response.Content.ReadAsStringAsync(CancellationToken.None));
    }

    /// <summary>
    /// A model declaring no accepted set takes any type. The set is a per-field
    /// declaration, not a default the framework imposes on every file field.
    /// </summary>
    [Fact]
    public async Task A_file_field_declaring_no_accepted_set_takes_any_content_type()
    {
        await using FormHost host = await FormHost.StartAsync();

        using MultipartFormDataContent body = Multipart(
            ("document", "avatar.png", "image/png", "not really a png"));

        using HttpResponseMessage response = await host.PostAsync("/optional", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A body that is not a well-formed multipart body at all. It has to answer 400:
    /// the read raises the same exception type an over-limit body does, and 413 would
    /// tell a client to send less of something that was never the wrong size.
    /// </summary>
    [Fact]
    public async Task A_multipart_body_carrying_no_parts_is_a_client_error_and_not_too_large()
    {
        await using FormHost host = await FormHost.StartAsync();

        using var body = new MultipartFormDataContent();

        using HttpResponseMessage response = await host.PostAsync("/document", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static MultipartFormDataContent Multipart(
        (string Field, string FileName, string ContentType, string Content)? file = null,
        string? caption = null)
    {
        var body = new MultipartFormDataContent();

        if (caption is not null)
        {
            body.Add(new StringContent(caption), "caption");
        }

        if (file is { } part)
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(part.Content));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(part.ContentType);
            body.Add(content, part.Field, part.FileName);
        }

        return body;
    }
}
