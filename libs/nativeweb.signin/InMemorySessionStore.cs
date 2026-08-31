using System.Collections.Concurrent;

namespace NativeWeb.SignIn;

/// <summary>
/// An <see cref="ISessionStore"/> held in this process's memory. Enough for the test suite and for
/// a single-instance deployment, and nothing more.
/// </summary>
/// <remarks>
/// Two limits, both intrinsic rather than incidental: sessions do not survive a restart, and a
/// second instance does not see them. Anything running behind more than one process wants a store
/// backed by something the processes share.
/// <para>
/// The compare-and-swap holds a lock across the compare and the write. A
/// <see cref="ConcurrentDictionary{TKey, TValue}"/>'s own <c>TryUpdate</c> compares the WHOLE
/// value, not the one field this contract compares, so it would report a conflict for two callers
/// whose refresh tokens agree and whose expiries differ by a tick.
/// </para>
/// </remarks>
public sealed class InMemorySessionStore(TimeProvider clock) : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionRecord> _rows = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <inheritdoc/>
    public Task CreateAsync(string keyHash, SessionRecord record, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHash);
        ArgumentNullException.ThrowIfNull(record);

        _rows[keyHash] = record;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<SessionRecord?> GetAsync(string keyHash, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHash);

        return Task.FromResult(_rows.TryGetValue(keyHash, out SessionRecord? row) ? row : null);
    }

    /// <inheritdoc/>
    public Task<SessionReplace> ReplaceIfMatchAsync(
        string keyHash,
        string expectedRefreshTokenHash,
        SessionRecord replacement,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHash);
        ArgumentNullException.ThrowIfNull(expectedRefreshTokenHash);
        ArgumentNullException.ThrowIfNull(replacement);

        lock (_gate)
        {
            if (!_rows.TryGetValue(keyHash, out SessionRecord? current))
            {
                return Task.FromResult(SessionReplace.Gone);
            }

            if (!string.Equals(current.RefreshTokenHash, expectedRefreshTokenHash, StringComparison.Ordinal))
            {
                return Task.FromResult(SessionReplace.Conflict);
            }

            _rows[keyHash] = replacement;
            return Task.FromResult(SessionReplace.Replaced);
        }
    }

    /// <inheritdoc/>
    public Task RemoveAsync(string keyHash, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHash);

        lock (_gate)
        {
            _rows.TryRemove(keyHash, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SweepAsync(CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        lock (_gate)
        {
            foreach (KeyValuePair<string, SessionRecord> row in _rows)
            {
                if (row.Value.ExpiresAt <= now)
                {
                    _rows.TryRemove(row.Key, out _);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>How many rows are held. For tests and for a health page; not part of the seam.</summary>
    public int Count => _rows.Count;
}
