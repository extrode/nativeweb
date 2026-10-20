using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using NativeWeb.Scaffold;

using Xunit;

namespace NativeWeb.Scaffold.Tests.Unit;

/// <summary>
/// <see cref="ScaffoldAccess.CanReadAsync"/>, which decides whether a guarded model's
/// link appears on the admin index. Until spec 018 the only tests over it asserted
/// that the generated source contained the call: nothing executed the decision, so a
/// mutation run left all fifteen of its mutants alive. The rule it has to mirror is
/// <c>ScaffoldEmitter.Authorization</c> -- policy wins over role, role means the role,
/// bare <c>[Auth]</c> means any authenticated identity.
/// </summary>
public sealed class ScaffoldAccessTests
{
    private sealed class StubAuthorization(bool succeeds) : IAuthorizationService
    {
        public string? LastPolicy { get; private set; }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(succeeds ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            this.LastPolicy = policyName;
            return Task.FromResult(succeeds ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        }
    }

    private static DefaultHttpContext Context(ClaimsPrincipal user, IAuthorizationService? authorization = null)
    {
        var services = new ServiceCollection();

        if (authorization is not null)
        {
            services.AddSingleton(authorization);
        }

        return new DefaultHttpContext
        {
            User = user,
            RequestServices = services.BuildServiceProvider(),
        };
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal SignedIn(params string[] roles) =>
        new(new ClaimsIdentity(
            [.. roles.Select(r => new Claim(ClaimTypes.Role, r))],
            "TestScheme",
            ClaimTypes.Name,
            ClaimTypes.Role));

    [Fact]
    public async Task AnOpenModel_IsReadableAnonymously() =>
        Assert.True(await ScaffoldAccess.CanReadAsync(Context(Anonymous()), null, null, false));

    [Fact]
    public async Task AnOpenModel_IgnoresAPolicyAndRoleThatWereNotDemanded() =>
        Assert.True(await ScaffoldAccess.CanReadAsync(
            Context(Anonymous(), new StubAuthorization(false)), "admins", "admin", false));

    [Fact]
    public async Task BareAuth_HidesTheModelFromAnAnonymousCaller() =>
        Assert.False(await ScaffoldAccess.CanReadAsync(Context(Anonymous()), null, null, true));

    [Fact]
    public async Task BareAuth_ShowsTheModelToAnyAuthenticatedIdentity() =>
        Assert.True(await ScaffoldAccess.CanReadAsync(Context(SignedIn()), null, null, true));

    [Fact]
    public async Task ARole_IsHiddenFromAnIdentityThatDoesNotHoldIt() =>
        Assert.False(await ScaffoldAccess.CanReadAsync(Context(SignedIn("editor")), null, "admin", true));

    [Fact]
    public async Task ARole_IsShownToAnIdentityThatHoldsIt() =>
        Assert.True(await ScaffoldAccess.CanReadAsync(Context(SignedIn("editor", "admin")), null, "admin", true));

    [Fact]
    public async Task ARole_IsHiddenFromAnAnonymousCaller() =>
        Assert.False(await ScaffoldAccess.CanReadAsync(Context(Anonymous()), null, "admin", true));

    [Fact]
    public async Task AnEmptyRole_MeansAnyAuthenticatedIdentity() =>
        Assert.True(await ScaffoldAccess.CanReadAsync(Context(SignedIn()), null, "", true));

    [Fact]
    public async Task APolicyThatSucceeds_ShowsTheModel() =>
        Assert.True(await ScaffoldAccess.CanReadAsync(
            Context(SignedIn(), new StubAuthorization(true)), "admins", null, true));

    [Fact]
    public async Task APolicyThatFails_HidesTheModel() =>
        Assert.False(await ScaffoldAccess.CanReadAsync(
            Context(SignedIn("admin"), new StubAuthorization(false)), "admins", "admin", true));

    [Fact]
    public async Task APolicy_IsEvaluatedByName()
    {
        var authorization = new StubAuthorization(true);

        await ScaffoldAccess.CanReadAsync(Context(SignedIn(), authorization), "admins", null, true);

        Assert.Equal("admins", authorization.LastPolicy);
    }

    [Fact]
    public async Task APolicy_WinsOverARoleTheIdentityDoesNotHold() =>
        Assert.True(await ScaffoldAccess.CanReadAsync(
            Context(SignedIn("editor"), new StubAuthorization(true)), "admins", "admin", true));

    [Fact]
    public async Task APolicy_IsEvaluatedEvenForAnAnonymousCaller() =>
        Assert.True(await ScaffoldAccess.CanReadAsync(
            Context(Anonymous(), new StubAuthorization(true)), "admins", null, true));

    [Fact]
    public async Task APolicyWithNoAuthorizationServiceRegistered_HidesTheModel() =>
        Assert.False(await ScaffoldAccess.CanReadAsync(
            Context(SignedIn("admin")), "admins", "admin", true));

    [Fact]
    public async Task AnEmptyPolicy_FallsThroughToTheRoleCheck() =>
        Assert.False(await ScaffoldAccess.CanReadAsync(
            Context(SignedIn("editor"), new StubAuthorization(true)), "", "admin", true));

    [Fact]
    public async Task ANullContext_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await ScaffoldAccess.CanReadAsync(null!, null, null, true));
}
