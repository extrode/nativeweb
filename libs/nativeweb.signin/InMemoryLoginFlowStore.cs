using System.Collections.Concurrent;

namespace NativeWeb.SignIn;

/// <summary>
/// An <see cref="ILoginFlowStore"/> held in this process's memory. Enough for the test suite and
/// for a single-instance deployment, and nothing more.
/// </summary>
/// <remarks>
/// A flow started on one instance cannot be taken on another, so behind more than one process this
/// store turns an ordinary sign-in into <see cref="FlowTake.Unknown"/> whenever the callback lands
/// on a different instance from the redirect. A product running more than one process wants a
/// store the processes share.
/// <para>
/// <see cref="TakeAsync"/> removes the row before judging it, which is what makes a second call
/// refuse even when the two race: the loser gets nothing back from
/// <see cref="ConcurrentDictionary{TKey, TValue}.TryRemove(TKey, out TValue)"/> and reports
/// <see cref="FlowTake.AlreadyUsed"/>. Judging first and removing second passes every
/// single-threaded test and lets two concurrent callbacks both consume one flow.
/// </para>
/// </remarks>
/// <param name="clock">The clock expiry is judged against.</param>
public sealed class InMemoryLoginFlowStore(TimeProvider clock) : ILoginFlowStore
{
    private readonly ConcurrentDictionary<string, Row> _rows = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task StartAsync(
        string state,
        string verifier,
        string returnTo,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifier);
        ArgumentNullException.ThrowIfNull(returnTo);

        _rows[state] = new Row(verifier, returnTo, expiresAt);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<FlowTakeResult> TakeAsync(string state, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        if (!_rows.TryRemove(state, out Row? row))
        {
            // Never started, already taken, or swept. Which of those it was is not recoverable
            // once the row is gone, and the seam's contract says it need not be.
            return Task.FromResult(FlowTakeResult.AlreadyUsed());
        }

        return Task.FromResult(
            row.ExpiresAt <= clock.GetUtcNow()
                ? FlowTakeResult.Expired()
                : FlowTakeResult.Taken(new LoginFlow(row.Verifier, row.ReturnTo)));
    }

    /// <inheritdoc/>
    public Task SweepAsync(CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        foreach (KeyValuePair<string, Row> row in _rows)
        {
            if (row.Value.ExpiresAt <= now)
            {
                _rows.TryRemove(row.Key, out _);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>How many flows are outstanding. For tests and for a health page; not part of the seam.</summary>
    public int Count => _rows.Count;

    private sealed record Row(string Verifier, string ReturnTo, DateTimeOffset ExpiresAt);
}
