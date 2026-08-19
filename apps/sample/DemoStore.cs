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

    protected abstract bool Matches(T item, string search);

    protected virtual void OnCreate(T item)
    {
    }

    protected virtual string NewKey() => Guid.NewGuid().ToString("n")[..8];

    public ValueTask<IReadOnlyList<T>> ListAsync(string? search, int skip, int take, CancellationToken ct)
    {
        IReadOnlyList<T> page = Filter(search)
            .OrderBy(KeyOf, StringComparer.Ordinal)
            .Skip(skip)
            .Take(take)
            .ToList();

        return ValueTask.FromResult(page);
    }

    public ValueTask<int> CountAsync(string? search, CancellationToken ct) =>
        ValueTask.FromResult(Filter(search).Count());

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
        if (!this._items.ContainsKey(id))
        {
            return ValueTask.FromResult(false);
        }

        AssignKey(item, id);
        this._items[id] = item;
        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> DeleteAsync(string id, CancellationToken ct) =>
        ValueTask.FromResult(this._items.TryRemove(id, out _));

    /// <summary>Seeds a row directly, bypassing key generation.</summary>
    internal void Seed(T item) => this._items[KeyOf(item)] = item;

    private IEnumerable<T> Filter(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? this._items.Values
            : this._items.Values.Where(i => Matches(i, search!));
}

internal sealed class ArticleStore : MemoryStore<Article>
{
    protected override string KeyOf(Article item) => item.Id;

    protected override void AssignKey(Article item, string key) => item.Id = key;

    // The generator marks CreatedAt read-only, so the form never carries it and
    // the store is the only thing that can set it. That is the intended division.
    protected override void OnCreate(Article item) => item.CreatedAt = DateTime.UtcNow;

    protected override bool Matches(Article item, string search) =>
        item.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
        || item.Body.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (item.Author?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
}

internal sealed class TagStore : MemoryStore<Tag>
{
    protected override string KeyOf(Tag item) => item.Slug;

    protected override void AssignKey(Tag item, string key) => item.Slug = key;

    protected override bool Matches(Tag item, string search) =>
        item.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
}
