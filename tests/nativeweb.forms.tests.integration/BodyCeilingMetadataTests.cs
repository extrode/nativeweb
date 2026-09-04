using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

using Xunit;

namespace NativeWeb.Forms.Tests.Integration;

/// <summary>
/// Spec 027 R4: the declared ceiling reaches routing, where it is still writable.
/// </summary>
/// <remarks>
/// <para>
/// The binder cannot raise the request-body limit itself. It runs after routing, after
/// the antiforgery middleware has read the form to find a hidden-field token, and by
/// then <c>IHttpMaxRequestBodySizeFeature.IsReadOnly</c> is true and the host-wide
/// ceiling has already refused the request. Measured 2026-09-04: a 300 KB upload to a
/// field declaring <c>MaxBytes = 1_048_576</c> was refused by the framework's own
/// 128 KiB default.
/// </para>
/// <para>
/// So the model publishes <c>MaxBodyBytes</c> and <c>MapForm</c> turns it into
/// <c>IRequestSizeLimitMetadata</c>. Routing applies that immediately after matching.
/// What the ceiling then does to a real oversize body is asserted by the e2e tier,
/// against a published binary with Kestrel's limit in place; TestServer has no such
/// limit, so this tier can only assert that the metadata is there and carries the
/// declared number.
/// </para>
/// </remarks>
public sealed class BodyCeilingMetadataTests
{
    [Fact]
    public async Task A_model_with_a_file_field_publishes_its_ceiling_to_routing()
    {
        await using FormHost host = await FormHost.StartAsync();

        IRequestSizeLimitMetadata? limit = Metadata(host, "/document");

        Assert.NotNull(limit);
        Assert.Equal(4096L + (64 * 1024), limit.MaxRequestBodySize);
    }

    [Fact]
    public async Task A_model_with_no_file_field_leaves_the_host_ceiling_alone()
    {
        await using FormHost host = await FormHost.StartAsync();

        Assert.Null(Metadata(host, "/caption"));
    }

    private static IRequestSizeLimitMetadata? Metadata(FormHost host, string route) =>
        host.Endpoints
            .Single(endpoint => endpoint.DisplayName?.Contains(route, StringComparison.Ordinal) == true)
            .Metadata
            .GetMetadata<IRequestSizeLimitMetadata>();
}
