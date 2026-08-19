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
    /// A page of rows, optionally filtered by <paramref name="search"/>, which the
    /// implementation matches against whatever the model marked searchable.
    /// </summary>
    ValueTask<IReadOnlyList<T>> ListAsync(string? search, int skip, int take, CancellationToken ct);

    /// <summary>Total matching <paramref name="search"/>, for paging.</summary>
    ValueTask<int> CountAsync(string? search, CancellationToken ct);

    /// <summary>One row by key, or null.</summary>
    ValueTask<T?> GetAsync(string id, CancellationToken ct);

    /// <summary>Insert. Returns the key of the new row.</summary>
    ValueTask<string> CreateAsync(T item, CancellationToken ct);

    /// <summary>Update by key. False if no such row.</summary>
    ValueTask<bool> UpdateAsync(string id, T item, CancellationToken ct);

    /// <summary>Delete by key. False if no such row.</summary>
    ValueTask<bool> DeleteAsync(string id, CancellationToken ct);
}
