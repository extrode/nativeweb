using Xunit;

namespace NativeWeb.SignIn.Testing;

/// <summary>
/// The behaviour every <see cref="ISessionStore"/> must have. Derive, supply a store, run.
/// </summary>
/// <remarks>
/// <see cref="ReplaceIfMatchAsync_UnderConcurrency_ReplacesExactlyOnce"/> is the case worth
/// deriving this class for. A read-then-compare-then-write store passes every other fixture here
/// and still lets two requests both refresh one session, which a rotating provider reads as a
/// replayed refresh token and answers by revoking the whole family. Use
/// <c>UPDATE … WHERE refresh_token_hash = @expected</c> and report on the row count.
/// </remarks>
public abstract class SessionStoreConformance
{
    /// <summary>Builds an empty store reading the supplied clock.</summary>
    /// <param name="clock">The clock the store must judge expiry against, where it judges any.</param>
    /// <returns>A store with no sessions in it.</returns>
    protected abstract ISessionStore CreateStore(TimeProvider clock);

    [Fact]
    public async Task GetAsync_ARowJustCreated_ReturnsIt()
    {
        var clock = new ConformanceClock();
        ISessionStore store = CreateStore(clock);
        SessionRecord record = Live(clock);
        await store.CreateAsync("hash-1", record, default);

        SessionRecord? read = await store.GetAsync("hash-1", default);

        Assert.NotNull(read);
        Assert.Equal(record.Subject, read.Subject);
        Assert.Equal(record.ExpiresAt, read.ExpiresAt);
        Assert.Equal(record.RefreshTokenHash, read.RefreshTokenHash);
    }

    [Fact]
    public async Task GetAsync_AHashNothingCreated_IsNull()
    {
        ISessionStore store = CreateStore(new ConformanceClock());

        Assert.Null(await store.GetAsync("never-created", default));
    }

    [Fact]
    public async Task GetAsync_TwoRows_KeepsThemApart()
    {
        var clock = new ConformanceClock();
        ISessionStore store = CreateStore(clock);
        await store.CreateAsync("hash-1", Live(clock) with { Subject = "subject-1" }, default);
        await store.CreateAsync("hash-2", Live(clock) with { Subject = "subject-2" }, default);

        Assert.Equal("subject-1", (await store.GetAsync("hash-1", default))!.Subject);
        Assert.Equal("subject-2", (await store.GetAsync("hash-2", default))!.Subject);
    }

    [Fact]
    public async Task GetAsync_APastExpiryRow_IsStillReturned()
    {
        var clock = new ConformanceClock();
        ISessionStore store = CreateStore(clock);
        await store.CreateAsync("hash-1", Live(clock), default);
        clock.Advance(TimeSpan.FromDays(30));

        Assert.NotNull(await store.GetAsync("hash-1", default));
    }

    [Fact]
    public async Task RemoveAsync_EndsTheSession()
    {
        var clock = new ConformanceClock();
        ISessionStore store = CreateStore(clock);
        await store.CreateAsync("hash-1", Live(clock), default);

        await store.RemoveAsync("hash-1", default);

        Assert.Null(await store.GetAsync("hash-1", default));
    }

    [Fact]
    public async Task RemoveAsync_ARowThatIsNotThere_IsNotAnError()
    {
        ISessionStore store = CreateStore(new ConformanceClock());

        await store.RemoveAsync("never-created", default);
    }

    [Fact]
    public async Task ReplaceIfMatchAsync_TheExpectedHash_Replaces()
    {
        var clock = new ConformanceClock();
        ISessionStore store = CreateStore(clock);
        await store.CreateAsync("hash-1", Live(clock), default);
        SessionRecord next = Live(clock) with { RefreshTokenHash = "refresh-2" };

        SessionReplace outcome = await store.ReplaceIfMatchAsync("hash-1", "refresh-1", next, default);

        Assert.Equal(SessionReplace.Replaced, outcome);
        Assert.Equal("refresh-2", (await store.GetAsync("hash-1", default))!.RefreshTokenHash);
    }

    [Fact]
    public async Task ReplaceIfMatchAsync_AStaleHash_Conflicts()
    {
        var clock = new ConformanceClock();
        ISessionStore store = CreateStore(clock);
        await store.CreateAsync("hash-1", Live(clock), default);
        SessionRecord next = Live(clock) with { RefreshTokenHash = "refresh-2" };

        SessionReplace outcome = await store.ReplaceIfMatchAsync(
            "hash-1", "somebody-elses-refresh", next, default);

        Assert.Equal(SessionReplace.Conflict, outcome);
    }

    [Fact]
    public async Task ReplaceIfMatchAsync_AStaleHash_LeavesTheRowAlone()
    {
        var clock = new ConformanceClock();
        ISessionStore store = CreateStore(clock);
        await store.CreateAsync("hash-1", Live(clock), default);

        await store.ReplaceIfMatchAsync(
            "hash-1", "somebody-elses-refresh", Live(clock) with { Subject = "overwritten" }, default);

        SessionRecord? read = await store.GetAsync("hash-1", default);
        Assert.Equal("subject-1", read!.Subject);
        Assert.Equal("refresh-1", read.RefreshTokenHash);
    }

    [Fact]
    public async Task ReplaceIfMatchAsync_ARowThatIsGone_ReportsGone()
    {
        var clock = new ConformanceClock();
        ISessionStore store = CreateStore(clock);

        SessionReplace outcome = await store.ReplaceIfMatchAsync(
            "never-created", "refresh-1", Live(clock), default);

        Assert.Equal(SessionReplace.Gone, outcome);
    }

    [Fact]
    public async Task ReplaceIfMatchAsync_UnderConcurrency_ReplacesExactlyOnce()
    {
        var clock = new ConformanceClock();
        ISessionStore store = CreateStore(clock);
        await store.CreateAsync("hash-1", Live(clock), default);

        SessionReplace[] outcomes = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(i => Task.Run(() => store.ReplaceIfMatchAsync(
                "hash-1",
                "refresh-1",
                Live(clock) with { RefreshTokenHash = $"refresh-{i}" },
                default))));

        Assert.Single(outcomes, o => o == SessionReplace.Replaced);
        Assert.All(
            outcomes.Where(o => o != SessionReplace.Replaced),
            o => Assert.Equal(SessionReplace.Conflict, o));
    }

    [Fact]
    public async Task SweepAsync_LeavesALiveRow()
    {
        var clock = new ConformanceClock();
        ISessionStore store = CreateStore(clock);
        await store.CreateAsync("hash-1", Live(clock), default);

        await store.SweepAsync(default);

        Assert.NotNull(await store.GetAsync("hash-1", default));
    }

    [Fact]
    public async Task SweepAsync_AnEmptyStore_IsNotAnError()
    {
        ISessionStore store = CreateStore(new ConformanceClock());

        await store.SweepAsync(default);
    }

    private static SessionRecord Live(TimeProvider clock) =>
        new(
            Subject: "subject-1",
            IssuedAt: clock.GetUtcNow(),
            ExpiresAt: clock.GetUtcNow().AddDays(14),
            ProtectedAccessToken: "protected-access",
            ProtectedRefreshToken: "protected-refresh",
            AccessTokenExpiresAt: clock.GetUtcNow().AddMinutes(30),
            RefreshTokenHash: "refresh-1");
}
