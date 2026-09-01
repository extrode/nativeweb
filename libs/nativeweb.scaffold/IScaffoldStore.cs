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
    /// <para>
    /// <paramref name="sortFields"/> is the <c>[ScaffoldSort]</c> set and is there for
    /// the same reason. <paramref name="sort"/> has already been checked against it, so
    /// an implementation does not validate; a name it does not recognise means a
    /// hand-written caller, and the answer is the default order rather than a throw.
    /// </para>
    /// <para>
    /// <b>Two obligations this interface cannot enforce and an implementation must
    /// meet.</b> First, <b>the key is the final tiebreaker on every order</b>. Keyset
    /// paging is defined by "the rows after this one", so an order that ranks two rows
    /// equally has no single answer to that, and rows are then repeated on one page and
    /// missing from the next -- which looks like data loss and is not. Second, <b>a
    /// nullable sorted column must fix where its NULLs go</b>, with an explicit
    /// <c>NULLS LAST</c> or <c>NULLS FIRST</c> and a predicate widened to carry those
    /// rows across the page boundary. <c>(author, id) &gt; (@author, @id)</c> is NULL,
    /// not true, for an author-less row, so without that widening every page after the
    /// first silently drops them. Both failures are invisible on a table small enough
    /// to fit one page, which is where they get written.
    /// </para>
    /// </remarks>
    /// <param name="search">The search term, or null for an unfiltered list.</param>
    /// <param name="searchFields">The model's <c>[ScaffoldSearch]</c> property names.</param>
    /// <param name="sort">
    /// The ordering, already validated against <paramref name="sortFields"/> by the
    /// caller, or null for the default order.
    /// </param>
    /// <param name="sortFields">The model's <c>[ScaffoldSort]</c> property names.</param>
    /// <param name="after">The position a previous page ended at, or null to start.</param>
    /// <param name="take">The maximum number of rows to return.</param>
    /// <param name="ct">The request's cancellation token.</param>
    /// <returns>Up to <paramref name="take"/> rows in the requested order.</returns>
    ValueTask<IReadOnlyList<T>> ListAsync(
        string? search,
        IReadOnlyList<string> searchFields,
        ScaffoldSortSpec? sort,
        IReadOnlyList<string> sortFields,
        ScaffoldCursor? after,
        int take,
        CancellationToken ct);

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
