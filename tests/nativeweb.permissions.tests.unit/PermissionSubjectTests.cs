using System.Security.Claims;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace NativeWeb.Permissions.Tests.Unit;

public sealed class PermissionSubjectTests
{
    private static ClaimsPrincipal Carrying(string claim, string value) =>
        new(new ClaimsIdentity([new Claim(claim, value)], "test"));

    private static PermissionSubjectResolver Resolver(Action<PermissionSubjectOptions>? configure = null)
    {
        var options = new PermissionSubjectOptions();
        configure?.Invoke(options);
        return new PermissionSubjectResolver(options);
    }

    [Fact]
    public void ConfiguringNothing_ReadsTheNameIdentifierClaim()
    {
        Assert.Equal("sub-1", Resolver().Of(Carrying(ClaimTypes.NameIdentifier, "sub-1")));
    }

    [Fact]
    public void ConfiguringNothing_FindsNothingUnderTheBareName()
    {
        Assert.Null(Resolver().Of(Carrying("sub", "sub-1")));
    }

    [Fact]
    public void ANamedClaim_IsRead()
    {
        Assert.Equal("sub-1", Resolver(options => options.ClaimName = "sub").Of(Carrying("sub", "sub-1")));
    }

    [Fact]
    public void ANamedClaim_ReplacesTheDefaultRatherThanAddingToIt()
    {
        Assert.Null(
            Resolver(options => options.ClaimName = "sub").Of(Carrying(ClaimTypes.NameIdentifier, "sub-1")));
    }

    [Fact]
    public void AResolver_AnswersInsteadOfAnyClaim()
    {
        ClaimsPrincipal user = new(new ClaimsIdentity(
            [new Claim("tenant", "acme"), new Claim("sub", "sub-1")],
            "test"));

        PermissionSubjectResolver resolver = Resolver(options =>
            options.Resolve = principal =>
                principal.FindFirstValue("tenant") + ":" + principal.FindFirstValue("sub"));

        Assert.Equal("acme:sub-1", resolver.Of(user));
    }

    [Fact]
    public void AResolverBesideTheDefaultClaimName_IsNotAConflict()
    {
        PermissionSubjectResolver resolver = Resolver(options => options.Resolve = _ => "sub-1");

        Assert.Equal("sub-1", resolver.Of(Carrying("anything", "ignored")));
    }

    [Fact]
    public void AResolverAndANamedClaim_IsRefusedNamingBoth()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Resolver(options =>
        {
            options.ClaimName = "sub";
            options.Resolve = _ => "sub-1";
        }));

        Assert.Contains("ClaimName", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Resolve", refused.Message, StringComparison.Ordinal);
        Assert.Contains("sub", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyClaimName_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => Resolver(options => options.ClaimName = "  "));
    }

    [Fact]
    public void AnUnauthenticatedPrincipal_HasNoSubjectAndNoResolverIsCalled()
    {
        bool called = false;
        PermissionSubjectResolver resolver = Resolver(options => options.Resolve = _ =>
        {
            called = true;
            return "sub-1";
        });

        Assert.Null(resolver.Of(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.False(called);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void AWhitespaceAnswer_IsNoSubject(string answer)
    {
        Assert.Null(Resolver(options => options.Resolve = _ => answer).Of(Carrying("sub", "ignored")));
    }

    [Fact]
    public void ANullAnswer_IsNoSubject()
    {
        Assert.Null(Resolver(options => options.Resolve = _ => null).Of(Carrying("sub", "ignored")));
    }

    [Fact]
    public void ASubjectWithSurroundingSpace_IsPassedThroughUntouched()
    {
        Assert.Equal(" sub-1 ", Resolver(options => options.Resolve = _ => " sub-1 ").Of(Carrying("sub", "x")));
    }

    [Fact]
    public void ARegistrationConfiguringBoth_FailsBeforeTheContainerIsUsed()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddPermissions(
            [new Permission("sample:article.view")],
            options =>
            {
                options.ClaimName = "sub";
                options.Resolve = _ => "sub-1";
            }));
    }

    [Fact]
    public void ARegistrationConfiguringNothing_ResolvesTheDefaultClaim()
    {
        var services = new ServiceCollection();
        services.AddPermissions([new Permission("sample:article.view")]);

        PermissionSubjectResolver resolver =
            services.BuildServiceProvider().GetRequiredService<PermissionSubjectResolver>();

        Assert.Equal("sub-1", resolver.Of(Carrying(ClaimTypes.NameIdentifier, "sub-1")));
    }
}
