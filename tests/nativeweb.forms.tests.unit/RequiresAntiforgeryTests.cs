using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Xunit;

namespace NativeWeb.Forms.Tests.Unit;

/// <summary>
/// The guard whose absence is silent. Brainstorm 001 §12 finding 3: a route bound through
/// <c>IBindableFromHttpContext</c> carries no antiforgery metadata of its own, so
/// <c>UseAntiforgery()</c> skips the endpoint and a form POST with no token at all is
/// answered 200 OK. These facts pin the metadata onto the route so that cannot recur.
/// </summary>
public sealed class RequiresAntiforgeryTests
{
    [Fact]
    public void The_metadata_requires_validation()
    {
        Assert.True(RequiresAntiforgery.Instance.RequiresValidation);
    }

    /// <summary>
    /// It has to be the framework's interface, not a look-alike: <c>UseAntiforgery()</c>
    /// looks up <see cref="IAntiforgeryMetadata"/> and nothing else.
    /// </summary>
    [Fact]
    public void The_metadata_is_the_interface_the_middleware_looks_up()
    {
        Assert.IsAssignableFrom<IAntiforgeryMetadata>(RequiresAntiforgery.Instance);
    }

    [Fact]
    public void The_instance_is_shared_because_it_carries_no_state()
    {
        Assert.Same(RequiresAntiforgery.Instance, RequiresAntiforgery.Instance);
    }

    [Fact]
    public void Requiring_it_attaches_the_metadata_to_the_route()
    {
        Endpoint endpoint = BuildEndpoint(route => route.RequireFormAntiforgery());

        IAntiforgeryMetadata? metadata = endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>();

        Assert.NotNull(metadata);
        Assert.True(metadata.RequiresValidation);
    }

    /// <summary>
    /// The control. Without the call the route carries nothing, which is the failure mode
    /// the T6 analyzer exists to make a build error.
    /// </summary>
    [Fact]
    public void A_route_that_does_not_require_it_carries_no_antiforgery_metadata()
    {
        Endpoint endpoint = BuildEndpoint(_ => { });

        Assert.Null(endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>());
    }

    [Fact]
    public void Requiring_it_returns_the_same_builder_so_it_chains()
    {
        WebApplication app = WebApplication.CreateBuilder().Build();
        RouteHandlerBuilder route = app.MapPost("/f", () => Results.Ok());

        Assert.Same(route, route.RequireFormAntiforgery());
    }

    [Fact]
    public void A_null_builder_is_a_programming_error()
    {
        Assert.Throws<ArgumentNullException>(
            () => FormRouteBuilderExtensions.RequireFormAntiforgery(null!));
    }

    private static Endpoint BuildEndpoint(Action<RouteHandlerBuilder> configure)
    {
        WebApplication app = WebApplication.CreateBuilder().Build();
        RouteHandlerBuilder route = app.MapPost("/f", () => Results.Ok());
        configure(route);

        EndpointDataSource source = Assert.Single(((IEndpointRouteBuilder)app).DataSources);
        return Assert.Single(source.Endpoints);
    }
}
