using NativeWeb.SignIn;

using Xunit;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class InMemoryLoginFlowStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TakeAsync_AfterStart_ReturnsTheFlow()
    {
        InMemoryLoginFlowStore store = Store();
        await store.StartAsync("state-1", "verifier-1", "/dashboard", Now.AddMinutes(15), default);

        FlowTakeResult taken = await store.TakeAsync("state-1", default);

        Assert.Equal(FlowTake.Taken, taken.Outcome);
        Assert.Equal("verifier-1", taken.Flow!.Value.Verifier);
        Assert.Equal("/dashboard", taken.Flow.Value.ReturnTo);
    }

    [Fact]
    public async Task TakeAsync_Twice_RefusesTheSecondCall()
    {
        InMemoryLoginFlowStore store = Store();
        await store.StartAsync("state-1", "verifier-1", "/", Now.AddMinutes(15), default);
        await store.TakeAsync("state-1", default);

        FlowTakeResult second = await store.TakeAsync("state-1", default);

        Assert.NotEqual(FlowTake.Taken, second.Outcome);
        Assert.Null(second.Flow);
    }

    [Fact]
    public async Task TakeAsync_UnknownState_Refuses()
    {
        FlowTakeResult taken = await Store().TakeAsync("never-started", default);

        Assert.NotEqual(FlowTake.Taken, taken.Outcome);
    }

    [Fact]
    public async Task TakeAsync_PastItsExpiry_IsExpiredAndCarriesNoFlow()
    {
        InMemoryLoginFlowStore store = Store();
        await store.StartAsync("state-1", "verifier-1", "/", Now.AddMinutes(-1), default);

        FlowTakeResult taken = await store.TakeAsync("state-1", default);

        Assert.Equal(FlowTake.Expired, taken.Outcome);
        Assert.Null(taken.Flow);
    }

    [Fact]
    public async Task TakeAsync_ExpiredButUnswept_StillRefuses()
    {
        InMemoryLoginFlowStore store = Store();
        await store.StartAsync("state-1", "verifier-1", "/", Now, default);

        Assert.Equal(FlowTake.Expired, (await store.TakeAsync("state-1", default)).Outcome);
    }

    [Fact]
    public async Task TakeAsync_FiftyConcurrentCallers_OnlyOneTakesTheFlow()
    {
        InMemoryLoginFlowStore store = Store();
        await store.StartAsync("state-1", "verifier-1", "/", Now.AddMinutes(15), default);
        using var start = new SemaphoreSlim(0, 50);

        Task<FlowTakeResult>[] racers =
        [
            .. Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
            {
                await start.WaitAsync();
                return await store.TakeAsync("state-1", default);
            })),
        ];

        start.Release(50);
        FlowTakeResult[] outcomes = await Task.WhenAll(racers);

        Assert.Equal(1, outcomes.Count(o => o.Outcome == FlowTake.Taken));
    }

    [Fact]
    public async Task SweepAsync_RemovesExpiredFlowsAndKeepsLiveOnes()
    {
        InMemoryLoginFlowStore store = Store();
        await store.StartAsync("expired", "v1", "/", Now.AddMinutes(-1), default);
        await store.StartAsync("live", "v2", "/", Now.AddMinutes(1), default);

        await store.SweepAsync(default);

        Assert.Equal(1, store.Count);
        Assert.Equal(FlowTake.Taken, (await store.TakeAsync("live", default)).Outcome);
    }

    [Fact]
    public async Task StartAsync_BlankState_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Store().StartAsync(" ", "verifier", "/", Now, default));
    }

    [Fact]
    public async Task StartAsync_BlankVerifier_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Store().StartAsync("state-1", "", "/", Now, default));
    }

    private static InMemoryLoginFlowStore Store() => new(new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
