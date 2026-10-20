using Xunit;

namespace NativeWeb.SignIn.Testing;

/// <summary>
/// The behaviour every <see cref="ILoginFlowStore"/> must have. Derive, supply a store, run.
/// </summary>
/// <remarks>
/// A flow store is at-most-once storage, and the case that decides whether an implementation is
/// safe is <see cref="TakeAsync_UnderConcurrency_TakesExactlyOnce"/>. A store that reads, judges
/// expiry, and then removes has a window in which two callers both see the flow — which is an
/// authorization code accepted twice. Removing BEFORE judging closes it.
/// </remarks>
public abstract class LoginFlowStoreConformance
{
    /// <summary>How far ahead a flow started by these fixtures expires.</summary>
    protected static TimeSpan Lifetime => TimeSpan.FromMinutes(15);

    /// <summary>Builds an empty store reading the supplied clock.</summary>
    /// <param name="clock">The clock the store must judge expiry against.</param>
    /// <returns>A store with no flows in it.</returns>
    protected abstract ILoginFlowStore CreateStore(TimeProvider clock);

    [Fact]
    public async Task TakeAsync_AFlowJustStarted_ReturnsItsVerifierAndReturnPath()
    {
        var clock = new ConformanceClock();
        ILoginFlowStore store = CreateStore(clock);
        await store.StartAsync("state-1", "verifier-1", "/dashboard", clock.GetUtcNow() + Lifetime, default);

        FlowTakeResult taken = await store.TakeAsync("state-1", default);

        Assert.Equal(FlowTake.Taken, taken.Outcome);
        Assert.Equal("verifier-1", taken.Flow!.Value.Verifier);
        Assert.Equal("/dashboard", taken.Flow.Value.ReturnTo);
    }

    [Fact]
    public async Task TakeAsync_AStateNothingStarted_IsRefused()
    {
        ILoginFlowStore store = CreateStore(new ConformanceClock());

        FlowTakeResult taken = await store.TakeAsync("never-started", default);

        // Unknown or AlreadyUsed, not one or the other. A store that removes before judging —
        // which is what makes a concurrent take safe — cannot tell "never existed" from "already
        // consumed" afterwards, and the contract does not ask it to.
        Assert.NotEqual(FlowTake.Taken, taken.Outcome);
        Assert.Null(taken.Flow);
    }

    [Fact]
    public async Task TakeAsync_ASecondTime_IsRefused()
    {
        var clock = new ConformanceClock();
        ILoginFlowStore store = CreateStore(clock);
        await store.StartAsync("state-1", "verifier-1", "/", clock.GetUtcNow() + Lifetime, default);
        await store.TakeAsync("state-1", default);

        FlowTakeResult second = await store.TakeAsync("state-1", default);

        Assert.NotEqual(FlowTake.Taken, second.Outcome);
        Assert.Null(second.Flow);
    }

    [Fact]
    public async Task TakeAsync_PastTheExpiry_IsRefused()
    {
        var clock = new ConformanceClock();
        ILoginFlowStore store = CreateStore(clock);
        await store.StartAsync("state-1", "verifier-1", "/", clock.GetUtcNow() + Lifetime, default);
        clock.Advance(Lifetime + TimeSpan.FromSeconds(1));

        FlowTakeResult taken = await store.TakeAsync("state-1", default);

        Assert.Equal(FlowTake.Expired, taken.Outcome);
        Assert.Null(taken.Flow);
    }

    [Fact]
    public async Task TakeAsync_OneOfTwoFlows_LeavesTheOther()
    {
        var clock = new ConformanceClock();
        ILoginFlowStore store = CreateStore(clock);
        await store.StartAsync("state-1", "verifier-1", "/", clock.GetUtcNow() + Lifetime, default);
        await store.StartAsync("state-2", "verifier-2", "/", clock.GetUtcNow() + Lifetime, default);

        await store.TakeAsync("state-1", default);
        FlowTakeResult other = await store.TakeAsync("state-2", default);

        Assert.Equal(FlowTake.Taken, other.Outcome);
        Assert.Equal("verifier-2", other.Flow!.Value.Verifier);
    }

    [Fact]
    public async Task TakeAsync_UnderConcurrency_TakesExactlyOnce()
    {
        var clock = new ConformanceClock();
        ILoginFlowStore store = CreateStore(clock);
        await store.StartAsync("state-1", "verifier-1", "/", clock.GetUtcNow() + Lifetime, default);

        FlowTakeResult[] outcomes = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ => Task.Run(() => store.TakeAsync("state-1", default))));

        Assert.Single(outcomes, o => o.Outcome == FlowTake.Taken);
    }

    [Fact]
    public async Task SweepAsync_LeavesALiveFlowTakeable()
    {
        var clock = new ConformanceClock();
        ILoginFlowStore store = CreateStore(clock);
        await store.StartAsync("state-1", "verifier-1", "/", clock.GetUtcNow() + Lifetime, default);

        await store.SweepAsync(default);

        Assert.Equal(FlowTake.Taken, (await store.TakeAsync("state-1", default)).Outcome);
    }

    [Fact]
    public async Task SweepAsync_AnEmptyStore_IsNotAnError()
    {
        ILoginFlowStore store = CreateStore(new ConformanceClock());

        await store.SweepAsync(default);
    }
}
