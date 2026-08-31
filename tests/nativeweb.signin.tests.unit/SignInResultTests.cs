using System.Reflection;

using NativeWeb.SignIn;

using Xunit;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class SignInResultTests
{
    [Fact]
    public void Ok_MatchesTheOkArmAndCarriesTheValue()
    {
        var result = SignInResult<string>.Ok("subject-1");

        Assert.Equal(SignInOutcome.Ok, result.Outcome);
        Assert.Equal(RefusalReason.None, result.Reason);
        Assert.Equal("value:subject-1", Describe(result));
    }

    [Fact]
    public void Refused_MatchesTheRefusedArmAndCarriesTheReason()
    {
        var result =
            SignInResult<string>.Refused(RefusalReason.InvalidGrant, "invalid_grant");

        Assert.Equal(SignInOutcome.Refused, result.Outcome);
        Assert.Equal(RefusalReason.InvalidGrant, result.Reason);
        Assert.Equal("refused:InvalidGrant", Describe(result));
    }

    [Fact]
    public void Unavailable_MatchesTheUnavailableArmAndCarriesNoReason()
    {
        var result = SignInResult<string>.Unavailable("timeout");

        Assert.Equal(SignInOutcome.Unavailable, result.Outcome);
        Assert.Equal(RefusalReason.None, result.Reason);
        Assert.Equal("unavailable", Describe(result));
    }

    [Fact]
    public void RefusedAndUnavailable_AreDistinguishable()
    {
        var refused = SignInResult<string>.Refused(RefusalReason.AccessDenied, "access_denied");
        var unavailable = SignInResult<string>.Unavailable("connection reset");

        Assert.NotEqual(refused.Outcome, unavailable.Outcome);
        Assert.NotEqual(Describe(refused), Describe(unavailable));
    }

    [Fact]
    public void Default_IsOkOnlyWhenConstructedSo()
    {
        Assert.Equal(RefusalReason.None, default(RefusalReason));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Match_WithANullDelegate_Throws(bool okNull, bool refusedNull, bool unavailableNull)
    {
        var result = SignInResult<string>.Ok("x");

        Assert.Throws<ArgumentNullException>(
            () => result.Match<int>(
                okNull ? null! : _ => 0,
                refusedNull ? null! : _ => 1,
                unavailableNull ? null! : () => 2));
    }

    [Fact]
    public void PublicSurface_CarriesNoProviderDetail()
    {
        string[] publicMembers = typeof(SignInResult<string>)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Where(n => n is not ("Equals" or "GetHashCode" or "ToString" or "GetType"))
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(["Match", "Outcome", "Reason", "get_Outcome", "get_Reason"], publicMembers);
    }

    [Fact]
    public void ProviderError_IsNotPubliclyReachable()
    {
        FieldInfo? field = typeof(SignInResult<string>).GetField(
            "ProviderError", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.False(field!.IsPublic);
    }

    [Fact]
    public void Refusal_DoesNotSurfaceTheProvidersWords()
    {
        var result =
            SignInResult<string>.Refused(RefusalReason.InvalidClient, "the client 'acme-web' is banned");

        string rendered = $"{result.Outcome}/{result.Reason}/{Describe(result)}";

        Assert.DoesNotContain("acme-web", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("banned", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("invalid_request", RefusalReason.InvalidRequest)]
    [InlineData("invalid_client", RefusalReason.InvalidClient)]
    [InlineData("unauthorized_client", RefusalReason.InvalidClient)]
    [InlineData("invalid_grant", RefusalReason.InvalidGrant)]
    [InlineData("access_denied", RefusalReason.AccessDenied)]
    [InlineData("server_error", RefusalReason.Unknown)]
    [InlineData("a_code_no_rfc_defines", RefusalReason.Unknown)]
    [InlineData("", RefusalReason.Unknown)]
    [InlineData(null, RefusalReason.Unknown)]
    public void From_MapsOnlyTheCodesTheLibraryOwns(string? providerError, RefusalReason expected)
    {
        Assert.Equal(expected, RefusalReasons.From(providerError));
    }

    private static string Describe(SignInResult<string> result) =>
        result.Match(
            value => $"value:{value}",
            reason => $"refused:{reason}",
            () => "unavailable");
}
