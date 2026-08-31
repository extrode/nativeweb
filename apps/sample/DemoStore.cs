using System.Collections.Concurrent;

using NativeWeb.Scaffold;

namespace NativeWeb.Sample;

/// <summary>
/// An in-memory <see cref="IScaffoldStore{T}"/> so the demo runs with no database.
/// </summary>
/// <remarks>
/// NOT the shape a real consumer ships: the project's binding rule is that all data
/// access goes through JauntyQ, and a production store implements these six methods
/// with JauntyQ queries. What this class demonstrates is that the generated
/// endpoints depend only on the contract -- swapping this for a JauntyQ-backed store
/// changes nothing above it, and no generated code has to be regenerated.
/// </remarks>
internal abstract class MemoryStore<T> : IScaffoldStore<T>
    where T : class
{
    private readonly ConcurrentDictionary<string, T> _items = new(StringComparer.Ordinal);

    protected abstract string KeyOf(T item);

    protected abstract void AssignKey(T item, string key);

    /// <summary>
    /// One searchable property's value, by the declared property name the generator
    /// passes down, or null for a name this model does not carry.
    /// </summary>
    /// <remarks>
    /// This replaced a hand-written <c>Matches</c> that named Title, Body and Author
    /// itself. The list it named and the list <c>[ScaffoldSearch]</c> declared were
    /// two copies of one fact, and nothing compared them: removing the attribute
    /// changed the search box's placeholder and left the column searchable, which is
    /// the defect this parameter exists to make impossible.
    /// </remarks>
    protected abstract string? Field(T item, string name);

    protected virtual void OnCreate(T item)
    {
    }

    protected virtual string NewKey() => Guid.NewGuid().ToString("n")[..8];

    public ValueTask<IReadOnlyList<T>> ListAsync(string? search, IReadOnlyList<string> searchFields, int skip, int take, CancellationToken ct)
    {
        // A negative skip is a caller error, and this store says so rather than
        // absorbing it. LINQ's Skip treats a negative count as zero, so before this
        // guard an overflowed page offset -- ?page=2000000000 times the page size,
        // wrapping past int.MaxValue -- quietly returned page one. A store over
        // PostgreSQL has no such mercy: OFFSET -1 is an error. The generated
        // endpoint clamps the page so neither happens, and this is what makes a
        // regression there visible here instead of only against a database.
        ArgumentOutOfRangeException.ThrowIfNegative(skip);

        IReadOnlyList<T> page = Filter(search, searchFields)
            .OrderBy(KeyOf, StringComparer.Ordinal)
            .Skip(skip)
            .Take(take)
            .ToList();

        return ValueTask.FromResult(page);
    }

    public ValueTask<int> CountAsync(string? search, IReadOnlyList<string> searchFields, CancellationToken ct) =>
        ValueTask.FromResult(Filter(search, searchFields).Count());

    public ValueTask<T?> GetAsync(string id, CancellationToken ct) =>
        ValueTask.FromResult(this._items.TryGetValue(id, out T? found) ? found : null);

    public ValueTask<string> CreateAsync(T item, CancellationToken ct)
    {
        string key = NewKey();
        AssignKey(item, key);
        OnCreate(item);
        this._items[key] = item;
        return ValueTask.FromResult(key);
    }

    public ValueTask<bool> UpdateAsync(string id, T item, CancellationToken ct)
    {
        // TryUpdate against the row we read, not ContainsKey then an indexer
        // assignment. The generated handler now treats false as 404, and it does so
        // because a concurrent delete between the load and the save is one request,
        // not a rare race -- a check-then-act here would resurrect the deleted row
        // and report success, which is the exact thing the handler was changed to
        // stop reporting. JauntyArticleStore gets this right through rows-affected;
        // the reference store has to match the semantics it demonstrates.
        //
        // The loop is what keeps false meaning "no such row" and nothing else. A
        // single TryUpdate also fails when a concurrent UPDATE replaced the value,
        // because it compares by reference; IScaffoldStore.UpdateAsync defines false
        // strictly as absent and the handler maps it to 404, so two admins saving
        // the same row left the loser told the record does not exist while it was
        // still in the list. Retrying until the row is either gone or ours makes the
        // concurrent-update case last-write-wins, which is what a SQL UPDATE's
        // rows-affected reports.
        AssignKey(item, id);

        while (this._items.TryGetValue(id, out T? current))
        {
            if (this._items.TryUpdate(id, item, current))
            {
                return ValueTask.FromResult(true);
            }
        }

        return ValueTask.FromResult(false);
    }

    public ValueTask<bool> DeleteAsync(string id, CancellationToken ct) =>
        ValueTask.FromResult(this._items.TryRemove(id, out _));

    /// <summary>Seeds a row directly, bypassing key generation.</summary>
    internal void Seed(T item) => this._items[KeyOf(item)] = item;

    private IEnumerable<T> Filter(string? search, IReadOnlyList<string> searchFields) =>
        string.IsNullOrWhiteSpace(search) || searchFields.Count == 0
            ? this._items.Values
            : this._items.Values.Where(i => Matches(i, search!, searchFields));

    private bool Matches(T item, string search, IReadOnlyList<string> searchFields)
    {
        foreach (string name in searchFields)
        {
            if (Field(item, name) is string value
                && value.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class ArticleStore : MemoryStore<Article>
{
    protected override string KeyOf(Article item) => item.Id;

    protected override void AssignKey(Article item, string key) => item.Id = key;

    // The generator marks CreatedAt read-only, so the form never carries it and
    // the store is the only thing that can set it. That is the intended division.
    protected override void OnCreate(Article item) => item.CreatedAt = DateTime.UtcNow;

    protected override string? Field(Article item, string name) => name switch
    {
        nameof(Article.Title) => item.Title,
        nameof(Article.Author) => item.Author,
        nameof(Article.Body) => item.Body,
        _ => null,
    };
}

internal sealed class TagStore : MemoryStore<Tag>
{
    protected override string KeyOf(Tag item) => item.Slug;

    protected override void AssignKey(Tag item, string key) => item.Slug = key;

    protected override string? Field(Tag item, string name) =>
        name == nameof(Tag.Name) ? item.Name : null;
}
