using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace NativeWeb.Permissions.Tests.Unit;

public sealed class EndpointFilterTests
{
    private static readonly Permission Change = new("sample:article.change");

    [Fact]
    public async Task NoSubject_Is401SoASignInCanHelp()
    {
        (EndpointFilterInvocationContext context, _) = await ContextAsync(anonymous: true);

        object? result = await new PermissionEndpointFilter(Change).InvokeAsync(context, Next);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
    }

    [Fact]
    public async Task SignedInWithoutTheGrant_Is403BecauseSigningInAgainWouldNotHelp()
    {
        (EndpointFilterInvocationContext context, _) = await ContextAsync(anonymous: false);

        object? result = await new PermissionEndpointFilter(Change).InvokeAsync(context, Next);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
    }

    [Fact]
    public async Task SignedInWithTheGrant_ReachesTheHandler()
    {
        (EndpointFilterInvocationContext context, CountingPermissionStore store) =
            await ContextAsync(anonymous: false);
        await store.GrantToSubjectAsync("sub-1", Change.Name, default);

        object? result = await new PermissionEndpointFilter(Change).InvokeAsync(context, Next);

        Assert.Equal("handler ran", result);
    }

    [Fact]
    public async Task GrantThroughAGroup_ReachesTheHandler()
    {
        (EndpointFilterInvocationContext context, CountingPermissionStore store) =
            await ContextAsync(anonymous: false);
        long group = await store.CreateGroupAsync("editors", default);
        await store.AddMemberAsync("sub-1", group, default);
        await store.GrantToGroupAsync(group, Change.Name, default);

        object? result = await new PermissionEndpointFilter(Change).InvokeAsync(context, Next);

        Assert.Equal("handler ran", result);
    }

    [Fact]
    public async Task ResourceRouteValue_IsReadFromTheRoute()
    {
        (EndpointFilterInvocationContext context, CountingPermissionStore store) =
            await ContextAsync(anonymous: false);
        context.HttpContext.Request.RouteValues["id"] = "row-7";
        await store.GrantToSubjectAsync("sub-1", Change.Name, default);

        object? result = await new PermissionEndpointFilter(Change, "id").InvokeAsync(context, Next);

        Assert.Equal("handler ran", result);
    }

    [Fact]
    public async Task PermissionLayerNotRegistered_ThrowsRatherThanAllowing()
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        var context = EndpointFilterInvocationContext.Create(http);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await new PermissionEndpointFilter(Change).InvokeAsync(context, Next));

        Assert.Contains("AddPermissions", thrown.Message, StringComparison.Ordinal);
    }

    private static ValueTask<object?> Next(EndpointFilterInvocationContext context) =>
        ValueTask.FromResult<object?>("handler ran");

    // The status is read off the result rather than by executing it: executing a
    // StatusCodeHttpResult resolves ILoggerFactory from RequestServices, which a
    // bare DefaultHttpContext does not have, and the refusal is the status.
    private static int StatusOf(object? result) =>
        ((IStatusCodeHttpResult)result!).StatusCode ?? 0;

    private static ValueTask<(EndpointFilterInvocationContext Context, CountingPermissionStore Store)> ContextAsync(
        bool anonymous)
    {
        var store = new CountingPermissionStore();

        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IPermissionStore>(store)
            .AddPermissions([Change])
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services.CreateScope().ServiceProvider };

        if (!anonymous)
        {
            http.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "sub-1")], "test"));
        }

        return ValueTask.FromResult((EndpointFilterInvocationContext.Create(http), store));
    }
}
