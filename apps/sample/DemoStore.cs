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

    /// <summary>
    /// One sortable property's value as something comparable, by the declared
    /// property name, falling back to the key for a name this model does not sort by.
    /// </summary>
    /// <remarks>
    /// A <c>switch</c> and not reflection, for the same reason <see cref="Field"/> is
    /// one: this runs inside a NativeAOT binary, where <c>GetProperty(name)</c>
    /// compiles and then returns null at run time. Returning the key by default is
    /// what makes an unrecognised property name the default order rather than a
    /// throw -- the generated endpoint has already rejected anything outside the
    /// allow-list, so a name arriving here came from a hand-written caller.
    /// </remarks>
    protected abstract IComparable? SortValue(T item, string name);

    protected virtual void OnCreate(T item)
    {
    }

    protected virtual string NewKey() => Guid.NewGuid().ToString("n")[..8];

    public ValueTask<IReadOnlyList<T>> ListAsync(
        string? search,
        IReadOnlyList<string> searchFields,
        ScaffoldSortSpec? sort,
        IReadOnlyList<string> sortFields,
        ScaffoldCursor? after,
        int take,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(take);

        string property = sort?.Property ?? string.Empty;
        bool descending = sort?.Direction == ScaffoldSortDirection.Descending;

        List<T> ordered = [.. Filter(search, searchFields)];
        ordered.Sort((a, b) => Compare(a, b, property, descending));

        // Located by key, not by comparing the cursor's sort value. This store holds
        // every row, so the boundary row is either present or gone, and finding it is
        // exact even when a hundred rows share the sorted value. A SQL store cannot
        // do this -- it has to express the boundary as a predicate, which is why
        // JauntyArticleStore parses the value and compares -- and the two are
        // equivalent only because the key breaks every tie in both.
        //
        // A boundary row that has since been deleted lands the reader on the first
        // page. That is a real behaviour difference from the SQL store, which keeps
        // its place because a value comparison does not need the row to exist.
        int at = after is ScaffoldCursor cursor
            ? ordered.FindIndex(item => string.Equals(KeyOf(item), cursor.Key, StringComparison.Ordinal))
            : -1;

        if (at < 0)
        {
            // One row short of `take` when the lost cursor was walking backwards. The
            // caller asks for PageSize + 1 to learn whether another page exists and
            // trims a backward page from its TAIL, so a full take here reads as an
            // overflowing backward page and the first row is trimmed away unseen.
            int first = after?.Backward == true ? Math.Max(take - 1, 0) : take;
            return ValueTask.FromResult<IReadOnlyList<T>>(ordered.GetRange(0, Math.Min(first, ordered.Count)));
        }

        if (after!.Value.Backward)
        {
            // The rows immediately before the boundary, still in display order. Taking
            // the FIRST `take` of everything before it would be a different page
            // entirely, and one the reader can reach from neither direction.
            int start = Math.Max(0, at - take);
            return ValueTask.FromResult<IReadOnlyList<T>>(ordered.GetRange(start, at - start));
        }

        int from = at + 1;
        return ValueTask.FromResult<IReadOnlyList<T>>(ordered.GetRange(from, Math.Min(take, ordered.Count - from)));
    }

    // Nulls last ascending, first descending -- PostgreSQL's own default, so the
    // reference store and the SQL store agree about where an author-less row sits.
    // The key breaks every tie, including between two nulls, or a page boundary
    // inside a block of equal values would have no single answer and the rows there
    // would repeat.
    private int Compare(T a, T b, string property, bool descending)
    {
        IComparable? x = SortValue(a, property);
        IComparable? y = SortValue(b, property);

        // Ordinal for text, because IComparable.CompareTo on a boxed string is
        // CurrentCulture: the same rows would come back in a different order on a
        // machine with a different locale, and the key tiebreaker below is ordinal
        // either way. Ordering is a fact about the data, not about the host.
        int result = (x, y) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            (string a1, string b1) => string.CompareOrdinal(a1, b1),
            _ => x.CompareTo(y),
        };

        if (result == 0)
        {
            result = string.CompareOrdinal(KeyOf(a), KeyOf(b));
        }

        return descending ? -result : result;
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

    protected override IComparable? SortValue(Article item, string name) => name switch
    {
        nameof(Article.Title) => item.Title,
        nameof(Article.Author) => item.Author,
        nameof(Article.CreatedAt) => item.CreatedAt,
        _ => item.Id,
    };
}

internal sealed class TagStore : MemoryStore<Tag>
{
    protected override string KeyOf(Tag item) => item.Slug;

    protected override void AssignKey(Tag item, string key) => item.Slug = key;

    protected override string? Field(Tag item, string name) =>
        name == nameof(Tag.Name) ? item.Name : null;

    protected override IComparable? SortValue(Tag item, string name) =>
        name == nameof(Tag.Name) ? item.Name : item.Slug;
}
