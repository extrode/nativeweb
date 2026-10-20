using NativeWeb.SignIn;

using Xunit;

namespace NativeWeb.SignIn.Tests.Unit;

public sealed class InMemorySessionStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_UnknownKey_IsNull()
    {
        InMemorySessionStore store = Store();

        Assert.Null(await store.GetAsync("nothing", default));
    }

    [Fact]
    public async Task CreateAsync_ThenGetAsync_ReturnsTheRow()
    {
        InMemorySessionStore store = Store();
        SessionRecord record = Record("rt-hash-1");

        await store.CreateAsync("key-1", record, default);

        Assert.Equal(record, await store.GetAsync("key-1", default));
    }

    [Fact]
    public async Task RemoveAsync_EndsTheSession()
    {
        InMemorySessionStore store = Store();
        await store.CreateAsync("key-1", Record("rt-hash-1"), default);

        await store.RemoveAsync("key-1", default);

        Assert.Null(await store.GetAsync("key-1", default));
    }

    [Fact]
    public async Task RemoveAsync_UnknownKey_IsNotAnError()
    {
        await Store().RemoveAsync("nothing", default);
    }

    [Fact]
    public async Task ReplaceIfMatchAsync_MatchingHash_Replaces()
    {
        InMemorySessionStore store = Store();
        await store.CreateAsync("key-1", Record("rt-hash-1"), default);
        SessionRecord replacement = Record("rt-hash-2");

        SessionReplace outcome = await store.ReplaceIfMatchAsync("key-1", "rt-hash-1", replacement, default);

        Assert.Equal(SessionReplace.Replaced, outcome);
        Assert.Equal(replacement, await store.GetAsync("key-1", default));
    }

    [Fact]
    public async Task ReplaceIfMatchAsync_StaleHash_ConflictsAndLeavesTheRowAlone()
    {
        InMemorySessionStore store = Store();
        SessionRecord current = Record("rt-hash-2");
        await store.CreateAsync("key-1", current, default);

        SessionReplace outcome =
            await store.ReplaceIfMatchAsync("key-1", "rt-hash-1", Record("rt-hash-3"), default);

        Assert.Equal(SessionReplace.Conflict, outcome);
        Assert.Equal(current, await store.GetAsync("key-1", default));
    }

    [Fact]
    public async Task ReplaceIfMatchAsync_MissingRow_IsGone()
    {
        SessionReplace outcome =
            await Store().ReplaceIfMatchAsync("key-1", "rt-hash-1", Record("rt-hash-2"), default);

        Assert.Equal(SessionReplace.Gone, outcome);
    }

    [Fact]
    public async Task ReplaceIfMatchAsync_FiftyConcurrentCallers_ExactlyOneWins()
    {
        InMemorySessionStore store = Store();
        await store.CreateAsync("key-1", Record("rt-hash-1"), default);
        using var start = new SemaphoreSlim(0, 50);

        Task<SessionReplace>[] racers =
        [
            .. Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
            {
                await start.WaitAsync();
                return await store.ReplaceIfMatchAsync("key-1", "rt-hash-1", Record($"rt-hash-{i + 2}"), default);
            })),
        ];

        start.Release(50);
        SessionReplace[] outcomes = await Task.WhenAll(racers);

        Assert.Equal(1, outcomes.Count(o => o == SessionReplace.Replaced));
        Assert.Equal(49, outcomes.Count(o => o == SessionReplace.Conflict));
    }

    [Fact]
    public async Task SweepAsync_RemovesExpiredRowsAndKeepsLiveOnes()
    {
        var clock = new FakeClock(Now);
        var store = new InMemorySessionStore(clock);
        await store.CreateAsync("expired", Record("rt-1") with { ExpiresAt = Now.AddMinutes(-1) }, default);
        await store.CreateAsync("live", Record("rt-2") with { ExpiresAt = Now.AddMinutes(1) }, default);

        await store.SweepAsync(default);

        Assert.Null(await store.GetAsync("expired", default));
        Assert.NotNull(await store.GetAsync("live", default));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task GetAsync_ExpiredButUnswept_StillReturnsTheRow()
    {
        var store = new InMemorySessionStore(new FakeClock(Now));
        await store.CreateAsync("expired", Record("rt-1") with { ExpiresAt = Now.AddMinutes(-1) }, default);

        Assert.NotNull(await store.GetAsync("expired", default));
    }

    [Fact]
    public void RecordToString_RedactsTheTokensAndTheHash()
    {
        string text = Record("rt-hash-secret").ToString();

        Assert.DoesNotContain("protected-access", text, StringComparison.Ordinal);
        Assert.DoesNotContain("protected-refresh", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rt-hash-secret", text, StringComparison.Ordinal);
        Assert.Contains("subject-1", text, StringComparison.Ordinal);
    }

    private static InMemorySessionStore Store() => new(new FakeClock(Now));

    private static SessionRecord Record(string refreshTokenHash) =>
        new(
            "subject-1",
            Now,
            Now.AddDays(14),
            "protected-access",
            "protected-refresh",
            Now.AddMinutes(30),
            refreshTokenHash);

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
