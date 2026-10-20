using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NativeWeb.SignIn;

using Xunit;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class SignInSweeperTests
{
    [Fact]
    public async Task SweepOnceAsync_HealthyStores_SweepsAllThree()
    {
        var flows = new CountingFlowStore();
        var sessions = new CountingSessionStore();
        var cache = new IntrospectionCache();
        cache.Set("stale", new IntrospectionVerdict(true, "who"), DateTimeOffset.UtcNow.AddMinutes(-1));

        await SweeperFor(flows, sessions, cache).SweepOnceAsync(CancellationToken.None);

        Assert.Equal(1, flows.Sweeps);
        Assert.Equal(1, sessions.Sweeps);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task SweepOnceAsync_FlowStoreThrows_StillSweepsTheOtherTwo()
    {
        var flows = new CountingFlowStore { Throw = new InvalidOperationException("the table is gone") };
        var sessions = new CountingSessionStore();
        var cache = new IntrospectionCache();
        cache.Set("stale", new IntrospectionVerdict(true, "who"), DateTimeOffset.UtcNow.AddMinutes(-1));

        await SweeperFor(flows, sessions, cache).SweepOnceAsync(CancellationToken.None);

        Assert.Equal(1, flows.Sweeps);
        Assert.Equal(1, sessions.Sweeps);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task SweepOnceAsync_SessionStoreThrows_DoesNotPropagate()
    {
        var sessions = new CountingSessionStore { Throw = new InvalidOperationException("gone") };

        await SweeperFor(new CountingFlowStore(), sessions, new IntrospectionCache())
            .SweepOnceAsync(CancellationToken.None);

        Assert.Equal(1, sessions.Sweeps);
    }

    [Fact]
    public async Task SweepOnceAsync_LiveIntrospectionVerdict_IsKept()
    {
        var cache = new IntrospectionCache();
        cache.Set("live", new IntrospectionVerdict(true, "who"), DateTimeOffset.UtcNow.AddMinutes(10));

        await SweeperFor(new CountingFlowStore(), new CountingSessionStore(), cache)
            .SweepOnceAsync(CancellationToken.None);

        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task SweepOnceAsync_StoreThrowsThenRecovers_SweepsAgain()
    {
        var flows = new CountingFlowStore { Throw = new InvalidOperationException("transient") };
        SignInSweeper sweeper = SweeperFor(flows, new CountingSessionStore(), new IntrospectionCache());

        await sweeper.SweepOnceAsync(CancellationToken.None);
        flows.Throw = null;
        await sweeper.SweepOnceAsync(CancellationToken.None);

        Assert.Equal(2, flows.Sweeps);
    }

    [Fact]
    public async Task ExecuteAsync_OverSeveralIntervals_SweepsRepeatedly()
    {
        var flows = new CountingFlowStore();
        SignInSweeper sweeper = SweeperFor(
            flows, new CountingSessionStore(), new IntrospectionCache(), TimeSpan.FromMilliseconds(15));
        using var stopping = new CancellationTokenSource();

        await sweeper.StartAsync(stopping.Token);
        await WaitUntil(() => flows.Sweeps >= 3, TimeSpan.FromSeconds(5));
        await stopping.CancelAsync();
        await sweeper.StopAsync(CancellationToken.None);

        Assert.True(flows.Sweeps >= 3, $"expected at least 3 sweeps, saw {flows.Sweeps}");
    }

    [Fact]
    public async Task ExecuteAsync_StoreAlwaysThrows_KeepsRunning()
    {
        var flows = new CountingFlowStore { Throw = new InvalidOperationException("always") };
        SignInSweeper sweeper = SweeperFor(
            flows, new CountingSessionStore(), new IntrospectionCache(), TimeSpan.FromMilliseconds(15));
        using var stopping = new CancellationTokenSource();

        await sweeper.StartAsync(stopping.Token);
        await WaitUntil(() => flows.Sweeps >= 3, TimeSpan.FromSeconds(5));
        await stopping.CancelAsync();
        await sweeper.StopAsync(CancellationToken.None);

        Assert.True(flows.Sweeps >= 3, $"expected at least 3 sweeps, saw {flows.Sweeps}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ctor_NonPositiveInterval_Throws(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SweeperFor(
                new CountingFlowStore(),
                new CountingSessionStore(),
                new IntrospectionCache(),
                TimeSpan.FromSeconds(seconds)));
    }

    private static SignInSweeper SweeperFor(
        ILoginFlowStore flows,
        ISessionStore sessions,
        IntrospectionCache cache,
        TimeSpan? interval = null)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton("test-scheme", flows);
        services.AddKeyedSingleton("test-scheme", sessions);
        services.AddKeyedSingleton("test-scheme", cache);
        ServiceProvider provider = services.BuildServiceProvider();

        return new SignInSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SignInSweeper>.Instance,
            TimeProvider.System,
            "test-scheme",
            interval ?? SignInSweeper.DefaultInterval);
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline && !condition())
        {
            await Task.Delay(5);
        }
    }

    private sealed class CountingFlowStore : ILoginFlowStore
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

    private sealed class CountingSessionStore : ISessionStore
    {
        private int _sweeps;

        public int Sweeps => Volatile.Read(ref _sweeps);

        public Exception? Throw { get; set; }

        public Task CreateAsync(string keyHash, SessionRecord record, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<SessionRecord?> GetAsync(string keyHash, CancellationToken ct) =>
            Task.FromResult<SessionRecord?>(null);

        public Task<SessionReplace> ReplaceIfMatchAsync(
            string keyHash,
            string expectedRefreshTokenHash,
            SessionRecord replacement,
            CancellationToken ct) =>
            Task.FromResult(SessionReplace.Gone);

        public Task RemoveAsync(string keyHash, CancellationToken ct) => Task.CompletedTask;

        public Task SweepAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _sweeps);
            return Throw is null ? Task.CompletedTask : Task.FromException(Throw);
        }
    }
}
