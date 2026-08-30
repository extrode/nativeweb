using Microsoft.AspNetCore.Http;

using Xunit;

namespace NativeWeb.Forms.Tests.Unit;

/// <summary>
/// The two guards a generated binder calls, tested against a real
/// <see cref="DefaultHttpContext"/> rather than a mock, because the thing under test IS
/// the framework's own content-type parsing.
/// </summary>
public sealed class FormBindingTests
{
    /// <summary>Both form content types the spec names, and neither may throw.</summary>
    public static TheoryData<string> FormContentTypes() => new()
    {
        "application/x-www-form-urlencoded",
        "application/x-www-form-urlencoded; charset=utf-8",
        "multipart/form-data; boundary=----abc",
    };

    /// <summary>The ones that must be rejected, including the empty case.</summary>
    public static TheoryData<string?> NonFormContentTypes() => new()
    {
        null,
        "",
        "application/json",
        "text/plain",
        "application/x-www-form-urlencodedX",
    };

    [Theory]
    [MemberData(nameof(FormContentTypes))]
    public void A_form_post_passes_the_content_type_guard(string contentType)
    {
        DefaultHttpContext context = new();
        context.Request.ContentType = contentType;

        FormBinding.RequireFormContentType(context);
    }

    [Theory]
    [MemberData(nameof(NonFormContentTypes))]
    public void Anything_else_is_rejected_at_415(string? contentType)
    {
        DefaultHttpContext context = new();
        context.Request.ContentType = contentType;

        BadHttpRequestException thrown = Assert.Throws<BadHttpRequestException>(
            () => FormBinding.RequireFormContentType(context));

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, thrown.StatusCode);
    }

    /// <summary>
    /// The status is the whole point of throwing this type rather than any other: a binder
    /// cannot return a result, so the exception carries the status the client sees.
    /// </summary>
    [Fact]
    public void The_rejection_is_not_a_500()
    {
        DefaultHttpContext context = new();
        context.Request.ContentType = "application/json";

        BadHttpRequestException thrown = Assert.Throws<BadHttpRequestException>(
            () => FormBinding.RequireFormContentType(context));

        Assert.NotEqual(StatusCodes.Status500InternalServerError, thrown.StatusCode);
    }

    [Fact]
    public void A_null_context_is_a_programming_error_not_a_415()
    {
        Assert.Throws<ArgumentNullException>(() => FormBinding.RequireFormContentType(null!));
    }

    [Fact]
    public void A_declared_field_reads_its_value()
    {
        FormCollection form = new(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["username"] = "ada",
        });

        Assert.Equal("ada", FormBinding.Field(form, "username"));
    }

    [Fact]
    public void An_absent_field_is_null()
    {
        Assert.Null(FormBinding.Field(FormCollection.Empty, "username"));
    }

    /// <summary>
    /// Absent and empty are deliberately not distinguished; the existing endpoints treat
    /// both as "this is required", and a binder that split them would push the difference
    /// into every handler.
    /// </summary>
    [Fact]
    public void A_present_but_empty_field_is_the_empty_string_not_null()
    {
        FormCollection form = new(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["username"] = "",
        });

        Assert.Equal("", FormBinding.Field(form, "username"));
    }

    /// <summary>
    /// A repeated field is what a duplicated input name posts. The framework joins them;
    /// what matters here is that reading one does not throw.
    /// </summary>
    [Fact]
    public void A_repeated_field_does_not_throw()
    {
        FormCollection form = new(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["username"] = new(["ada", "grace"]),
        });

        Assert.Equal("ada,grace", FormBinding.Field(form, "username"));
    }

    [Fact]
    public void A_null_form_is_a_programming_error()
    {
        Assert.Throws<ArgumentNullException>(() => FormBinding.Field(null!, "username"));
    }
}
