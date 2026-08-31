namespace NativeWeb.Scaffold;

/// <summary>
/// The persistence seam the generated CRUD endpoints call.
/// </summary>
/// <remarks>
/// Deliberately six methods and no query language. NativeWeb does not have an ORM
/// and is not growing one -- JauntyQ owns data access, and the point of this
/// interface is that the generated endpoints depend on a contract rather than on
/// any particular way of satisfying it. A consumer implements it with JauntyQ
/// queries; the scaffolding neither knows nor cares.
/// <para>
/// Everything takes a <see cref="CancellationToken"/> because every implementation
/// that touches a database needs one, and an interface that omits it forces every
/// implementer to ignore request aborts.
/// </para>
/// </remarks>
/// <typeparam name="T">The scaffolded model.</typeparam>
public interface IScaffoldStore<T>
    where T : class
{
    /// <summary>
    /// A page of rows, optionally filtered by <paramref name="search"/> across the
    /// properties named in <paramref name="searchFields"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="searchFields"/> is the model's <c>[ScaffoldSearch]</c> set,
    /// as declared property names, passed by the generated endpoint. It is a
    /// parameter rather than something an implementation knows because the
    /// alternative was measured and is worse: before 2026-08-31 the interface took
    /// the search term alone, so every store re-declared the searchable columns by
    /// hand -- see the shape <c>ArticleStore.Matches</c> used to have -- and adding
    /// or removing a <c>[ScaffoldSearch]</c> attribute changed the placeholder text
    /// in the search box and nothing else. Nothing failed; the field simply stopped
    /// being searched. Empty when the model marks nothing searchable, in which case
    /// <paramref name="search"/> is always null and the box is not rendered.
    /// </remarks>
    ValueTask<IReadOnlyList<T>> ListAsync(string? search, IReadOnlyList<string> searchFields, int skip, int take, CancellationToken ct);

    /// <summary>Total matching <paramref name="search"/>, for paging.</summary>
    ValueTask<int> CountAsync(string? search, IReadOnlyList<string> searchFields, CancellationToken ct);

    /// <summary>One row by key, or null.</summary>
    ValueTask<T?> GetAsync(string id, CancellationToken ct);

    /// <summary>Insert. Returns the key of the new row.</summary>
    /// <remarks>
    /// <b>The implementation assigns the key, and it must be unique.</b> The key
    /// property of <paramref name="item"/> arrives unset -- a key that round-trips
    /// through a form is an id the client chooses, so the generator never binds one
    /// -- which leaves uniqueness with the only party that can see the other rows.
    /// A store returning a key it has used before overwrites a row silently; a
    /// store returning the empty string sends the caller to a page that does not
    /// exist, which is at least visible. The generated create redirects to the
    /// returned key, so a wrong answer here surfaces on the next request rather
    /// than at the next collision.
    /// </remarks>
    ValueTask<string> CreateAsync(T item, CancellationToken ct);

    /// <summary>Update by key. False if no such row.</summary>
    ValueTask<bool> UpdateAsync(string id, T item, CancellationToken ct);

    /// <summary>Delete by key. False if no such row.</summary>
    ValueTask<bool> DeleteAsync(string id, CancellationToken ct);
}
