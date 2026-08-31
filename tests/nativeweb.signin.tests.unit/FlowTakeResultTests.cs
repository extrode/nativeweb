using NativeWeb.SignIn;

using Xunit;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class FlowTakeResultTests
{
    private static readonly LoginFlow Flow = new("verifier-secret-value", "/dashboard");

    [Fact]
    public void Taken_CarriesTheFlow()
    {
        var result = FlowTakeResult.Taken(Flow);

        Assert.Equal(FlowTake.Taken, result.Outcome);
        Assert.Equal(Flow, result.Flow);
    }

    [Fact]
    public void Unknown_CarriesNoFlow()
    {
        var result = FlowTakeResult.Unknown();

        Assert.Equal(FlowTake.Unknown, result.Outcome);
        Assert.Null(result.Flow);
    }

    [Fact]
    public void AlreadyUsed_CarriesNoFlow()
    {
        var result = FlowTakeResult.AlreadyUsed();

        Assert.Equal(FlowTake.AlreadyUsed, result.Outcome);
        Assert.Null(result.Flow);
    }

    [Fact]
    public void Expired_CarriesNoFlow()
    {
        var result = FlowTakeResult.Expired();

        Assert.Equal(FlowTake.Expired, result.Outcome);
        Assert.Null(result.Flow);
    }

    [Fact]
    public void FlowTakeResult_HasNoPublicConstructor()
    {
        Assert.Empty(typeof(FlowTakeResult).GetConstructors());
    }

    [Fact]
    public void Equality_SameOutcomeAndFlow_AreEqual()
    {
        Assert.True(FlowTakeResult.Taken(Flow) == FlowTakeResult.Taken(Flow));
        Assert.Equal(FlowTakeResult.Unknown(), FlowTakeResult.Unknown());
    }

    [Fact]
    public void Equality_DifferentOutcome_AreNotEqual()
    {
        Assert.True(FlowTakeResult.Unknown() != FlowTakeResult.AlreadyUsed());
    }

    [Fact]
    public void LoginFlowToString_DoesNotContainTheVerifier()
    {
        string text = Flow.ToString();

        Assert.DoesNotContain("verifier-secret-value", text, StringComparison.Ordinal);
        Assert.Contains("redacted", text, StringComparison.Ordinal);
        Assert.Contains("/dashboard", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FlowTakeResultToString_DoesNotContainTheVerifier()
    {
        Assert.DoesNotContain(
            "verifier-secret-value",
            FlowTakeResult.Taken(Flow).ToString() ?? "",
            StringComparison.Ordinal);
    }
}
