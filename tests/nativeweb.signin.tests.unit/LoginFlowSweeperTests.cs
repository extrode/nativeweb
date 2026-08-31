using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NativeWeb.SignIn;

using Xunit;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class LoginFlowSweeperTests
{
    [Fact]
    public async Task SweepOnceAsync_HealthyStore_CallsSweep()
    {
        var store = new CountingStore();

        await SweeperFor(store).SweepOnceAsync(CancellationToken.None);

        Assert.Equal(1, store.Sweeps);
    }

    [Fact]
    public async Task SweepOnceAsync_StoreThrows_DoesNotPropagate()
    {
        var store = new CountingStore { Throw = new InvalidOperationException("the table is gone") };

        await SweeperFor(store).SweepOnceAsync(CancellationToken.None);

        Assert.Equal(1, store.Sweeps);
    }

    [Fact]
    public async Task SweepOnceAsync_StoreThrowsThenRecovers_SweepsAgain()
    {
        var store = new CountingStore { Throw = new InvalidOperationException("transient") };
        LoginFlowSweeper sweeper = SweeperFor(store);

        await sweeper.SweepOnceAsync(CancellationToken.None);
        store.Throw = null;
        await sweeper.SweepOnceAsync(CancellationToken.None);

        Assert.Equal(2, store.Sweeps);
    }

    [Fact]
    public async Task ExecuteAsync_OverSeveralIntervals_SweepsRepeatedly()
    {
        var store = new CountingStore();
        LoginFlowSweeper sweeper = SweeperFor(store, TimeSpan.FromMilliseconds(15));
        using var stopping = new CancellationTokenSource();

        await sweeper.StartAsync(stopping.Token);
        await WaitUntil(() => store.Sweeps >= 3, TimeSpan.FromSeconds(5));
        await stopping.CancelAsync();
        await sweeper.StopAsync(CancellationToken.None);

        Assert.True(store.Sweeps >= 3, $"expected at least 3 sweeps, saw {store.Sweeps}");
    }

    [Fact]
    public async Task ExecuteAsync_StoreAlwaysThrows_KeepsRunning()
    {
        var store = new CountingStore { Throw = new InvalidOperationException("always") };
        LoginFlowSweeper sweeper = SweeperFor(store, TimeSpan.FromMilliseconds(15));
        using var stopping = new CancellationTokenSource();

        await sweeper.StartAsync(stopping.Token);
        await WaitUntil(() => store.Sweeps >= 3, TimeSpan.FromSeconds(5));
        await stopping.CancelAsync();
        await sweeper.StopAsync(CancellationToken.None);

        Assert.True(store.Sweeps >= 3, $"expected at least 3 sweeps, saw {store.Sweeps}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ctor_NonPositiveInterval_Throws(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SweeperFor(new CountingStore(), TimeSpan.FromSeconds(seconds)));
    }

    private static LoginFlowSweeper SweeperFor(ILoginFlowStore store, TimeSpan? interval = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        ServiceProvider provider = services.BuildServiceProvider();

        return new LoginFlowSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LoginFlowSweeper>.Instance,
            TimeProvider.System,
            interval ?? LoginFlowSweeper.DefaultInterval);
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline && !condition())
        {
            await Task.Delay(5);
        }
    }

    private sealed class CountingStore : ILoginFlowStore
    {
        private int _sweeps;

        public int Sweeps => Volatile.Read(ref _sweeps);

        public Exception? Throw { get; set; }

        public Task StartAsync(
            string state, string verifier, string returnTo, DateTimeOffset expiresAt, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<FlowTakeResult> TakeAsync(string state, CancellationToken ct) =>
            Task.FromResult(FlowTakeResult.Unknown());

        public Task SweepAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _sweeps);
            return Throw is null ? Task.CompletedTask : Task.FromException(Throw);
        }
    }
}
