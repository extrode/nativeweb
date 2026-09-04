using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

using Xunit;

using HttpFormFile = Microsoft.AspNetCore.Http.FormFile;

namespace NativeWeb.Forms.Tests.Unit;

/// <summary>
/// The file half of the binder, spec 027 R1-R4. Against a real
/// <see cref="DefaultHttpContext"/> and a real <see cref="FormFileCollection"/> for the
/// reason <see cref="FormBindingTests"/> gives: the thing under test is the framework
/// types, so a mock of them would assert the mock.
/// </summary>
public sealed class FormFileBindingTests
{
    private const string Png = "image/png";

    [Fact]
    public void A_multipart_post_passes_the_multipart_guard()
    {
        DefaultHttpContext context = new();
        context.Request.ContentType = "multipart/form-data; boundary=----x";

        Exception? thrown = Record.Exception(() => FormBinding.RequireMultipart(context));

        Assert.Null(thrown);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("application/json")]
    [InlineData(null)]
    public void Anything_that_cannot_carry_a_file_is_415(string? contentType)
    {
        DefaultHttpContext context = new();
        context.Request.ContentType = contentType;

        BadHttpRequestException thrown = Assert.Throws<BadHttpRequestException>(
            () => FormBinding.RequireMultipart(context));

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, thrown.StatusCode);
    }

    /// <summary>
    /// Casing is the server's to ignore, not the client's to get right. A browser that
    /// sends Multipart/Form-Data is sending a multipart form.
    /// </summary>
    [Fact]
    public void The_multipart_guard_ignores_header_casing()
    {
        DefaultHttpContext context = new();
        context.Request.ContentType = "Multipart/Form-Data; boundary=----x";

        Assert.Null(Record.Exception(() => FormBinding.RequireMultipart(context)));
    }

    [Fact]
    public void The_declared_limit_reaches_the_form_feature()
    {
        DefaultHttpContext context = new();
        context.Request.ContentType = "multipart/form-data; boundary=----x";

        FormBinding.LimitBody(context, 1024);

        Assert.NotNull(context.Features.Get<IFormFeature>());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_limit_of_zero_or_less_is_refused(long maxBytes)
    {
        DefaultHttpContext context = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => FormBinding.LimitBody(context, maxBytes));
    }

    [Fact]
    public void A_posted_file_binds_its_name_type_length_and_content()
    {
        IFormCollection form = FormWith(Named("avatar", "portrait.png", Png, "the bytes"));

        FormFile? bound = FormBinding.File(form, "avatar", null);

        Assert.NotNull(bound);
        Assert.Equal("portrait.png", bound.Name);
        Assert.Equal(Png, bound.ContentType);
        Assert.Equal(9, bound.Length);
        using StreamReader reader = new(bound.OpenReadStream());
        Assert.Equal("the bytes", reader.ReadToEnd());
    }

    [Fact]
    public void An_optional_file_the_form_does_not_carry_binds_null()
    {
        Assert.Null(FormBinding.File(FormWith(), "avatar", null));
    }

    /// <summary>
    /// A browser posts an empty part for a file input the person left alone, so zero
    /// bytes is "no file" and not "a file of nothing".
    /// </summary>
    [Fact]
    public void An_empty_part_binds_null()
    {
        IFormCollection form = FormWith(Named("avatar", "portrait.png", Png, ""));

        Assert.Null(FormBinding.File(form, "avatar", null));
    }

    [Fact]
    public void A_required_file_that_is_absent_is_400()
    {
        BadHttpRequestException thrown = Assert.Throws<BadHttpRequestException>(
            () => FormBinding.RequiredFile(FormWith(), "avatar", null));

        Assert.Equal(StatusCodes.Status400BadRequest, thrown.StatusCode);
        Assert.Contains("avatar", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_required_file_that_is_present_binds()
    {
        IFormCollection form = FormWith(Named("avatar", "portrait.png", Png, "x"));

        Assert.Equal("portrait.png", FormBinding.RequiredFile(form, "avatar", null).Name);
    }

    [Fact]
    public void A_content_type_outside_the_accepted_set_is_415_naming_what_is_accepted()
    {
        IFormCollection form = FormWith(Named("avatar", "payload.zip", "application/zip", "x"));

        BadHttpRequestException thrown = Assert.Throws<BadHttpRequestException>(
            () => FormBinding.File(form, "avatar", [Png, "image/jpeg"]));

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, thrown.StatusCode);
        Assert.Contains(Png, thrown.Message, StringComparison.Ordinal);
        Assert.Contains("image/jpeg", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("application/zip", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_accepted_content_type_binds()
    {
        IFormCollection form = FormWith(Named("avatar", "portrait.png", Png, "x"));

        Assert.NotNull(FormBinding.File(form, "avatar", [Png]));
    }

    /// <summary>
    /// The header carries parameters and the declaration does not, so comparing the
    /// whole header would refuse a legitimate post.
    /// </summary>
    [Fact]
    public void Parameters_after_the_media_type_do_not_defeat_the_accepted_set()
    {
        IFormCollection form = FormWith(Named("avatar", "p.png", "image/PNG; charset=binary", "x"));

        Assert.NotNull(FormBinding.File(form, "avatar", [Png]));
    }

    [Fact]
    public void A_file_declaring_no_content_type_is_refused_by_a_set_that_names_one()
    {
        IFormCollection form = FormWith(Named("avatar", "p.png", string.Empty, "x"));

        BadHttpRequestException thrown = Assert.Throws<BadHttpRequestException>(
            () => FormBinding.File(form, "avatar", [Png]));

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, thrown.StatusCode);
    }

    [Fact]
    public void An_empty_accepted_set_accepts_anything()
    {
        IFormCollection form = FormWith(Named("avatar", "payload.zip", "application/zip", "x"));

        Assert.NotNull(FormBinding.File(form, "avatar", []));
    }

    /// <summary>
    /// The client chooses this string and it is shown to a person, so it reaches the
    /// handler unchanged. Nothing downstream may treat it as a path; R13 is where that
    /// is asserted.
    /// </summary>
    [Fact]
    public void A_traversal_shaped_file_name_reaches_the_handler_unchanged()
    {
        IFormCollection form = FormWith(Named("avatar", "../../etc/passwd", Png, "x"));

        Assert.Equal("../../etc/passwd", FormBinding.File(form, "avatar", null)?.Name);
    }

    private static HttpFormFile Named(string field, string fileName, string contentType, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        return new HttpFormFile(new MemoryStream(bytes), 0, bytes.Length, field, fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private static FormCollection FormWith(params HttpFormFile[] files)
    {
        FormFileCollection collection = [];
        foreach (HttpFormFile file in files)
        {
            collection.Add(file);
        }

        return new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), collection);
    }
}
